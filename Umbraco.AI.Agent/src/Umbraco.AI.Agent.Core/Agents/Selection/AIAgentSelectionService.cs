using Microsoft.Extensions.Logging;
using Umbraco.AI.Agent.Core.Agents;
using Umbraco.AI.Agent.Core.Surfaces;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Security;

namespace Umbraco.AI.Agent.Core.Agents.Selection;

/// <inheritdoc cref="IAIAgentSelectionService" />
internal sealed class AIAgentSelectionService : IAIAgentSelectionService
{
    private readonly IAIAgentService _agentService;
    private readonly AIAgentSurfaceCollection _surfaceCollection;
    private readonly AIAgentScopeValidator _scopeValidator;
    private readonly AIAgentSelectorCollection _selectorCollection;
    private readonly IEventAggregator _eventAggregator;
    private readonly IBackOfficeSecurityAccessor? _backOfficeSecurityAccessor;
    private readonly ILogger<AIAgentSelectionService> _logger;

    public AIAgentSelectionService(
        IAIAgentService agentService,
        AIAgentSurfaceCollection surfaceCollection,
        AIAgentScopeValidator scopeValidator,
        AIAgentSelectorCollection selectorCollection,
        IEventAggregator eventAggregator,
        ILogger<AIAgentSelectionService> logger,
        IBackOfficeSecurityAccessor? backOfficeSecurityAccessor = null)
    {
        _agentService = agentService;
        _surfaceCollection = surfaceCollection;
        _scopeValidator = scopeValidator;
        _selectorCollection = selectorCollection;
        _eventAggregator = eventAggregator;
        _logger = logger;
        _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
    }

    /// <inheritdoc />
    public async Task<AIAgentSelectionResult?> SelectAgentAsync(
        AIAgentSelectionInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var candidates = await GetCandidateAgentsAsync(input, cancellationToken);
        if (candidates.Count == 0)
        {
            return null;
        }

        var userGroupIds = await GetCurrentUserGroupIdsAsync(cancellationToken);
        var previousAgent = ResolvePreviousAgent(input.PreviousAgentId, candidates);

        var request = new AIAgentSelectionRequest
        {
            CandidateAgents = candidates,
            Messages = input.Messages,
            AvailabilityContext = input.AvailabilityContext,
            ContextItems = input.ContextItems,
            SurfaceId = input.SurfaceId,
            UserGroupIds = userGroupIds,
            FrontendTools = input.FrontendTools,
            PreviousAgent = previousAgent,
        };

        AIAgentSelectionResult selection;
        if (candidates.Count == 1)
        {
            // No selector gets a say - there's only one possible answer.
            selection = new AIAgentSelectionResult(candidates[0], AIAgentSelectorIds.OnlyCandidate, Reason: null);
        }
        else
        {
            var result = await RunSelectorChainAsync(request, cancellationToken);
            selection = result ?? new AIAgentSelectionResult(candidates[0], AIAgentSelectorIds.Fallback, Reason: null);
        }

        var notification = new AIAgentSelectedNotification(selection, request, new EventMessages());
        await _eventAggregator.PublishAsync(notification, cancellationToken);

        return selection;
    }

    /// <summary>
    /// Looks up the surface's agents and filters to active, scope-available candidates - today's
    /// <c>SelectAgentForPromptAsync</c> steps 1-3, moved unchanged.
    /// </summary>
    private async Task<IReadOnlyList<AIAgent>> GetCandidateAgentsAsync(
        AIAgentSelectionInput input,
        CancellationToken cancellationToken)
    {
        var allAgents = await _agentService.GetAgentsBySurfaceAsync(input.SurfaceId, cancellationToken);

        // Same rule an explicitly named agent goes through, so auto and named runs can't drift apart.
        return allAgents
            .Where(a => _scopeValidator.IsAgentAvailableOnSurface(
                a, input.SurfaceId, input.AvailabilityContext, _surfaceCollection))
            .ToList();
    }

    /// <summary>
    /// Runs the selector collection in order. Returns the first candidate a selector picks, or
    /// <c>null</c> if nobody decides.
    /// </summary>
    /// <remarks>
    /// A selector that throws is logged and skipped so one broken rule can't take down the whole
    /// chain - <b>except</b> when the exception is an <see cref="OperationCanceledException"/> raised
    /// because <paramref name="cancellationToken"/> was actually cancelled, which propagates instead.
    /// An <see cref="OperationCanceledException"/> can also come from something unrelated to our token
    /// (e.g. an <c>HttpClient</c> request timeout throws <see cref="TaskCanceledException"/> without the
    /// caller's token being cancelled) - that case is skipped like any other selector failure, not
    /// propagated.
    /// </remarks>
    private async Task<AIAgentSelectionResult?> RunSelectorChainAsync(
        AIAgentSelectionRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var selector in _selectorCollection)
        {
            AIAgentSelectionResult? result;
            try
            {
                result = await selector.SelectAgentAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(
                    ex,
                    "Agent selector {SelectorType} threw while selecting an agent for surface {SurfaceId}. Skipping it.",
                    selector.GetType(),
                    request.SurfaceId);
                continue;
            }

            if (result is null)
            {
                continue;
            }

            var candidate = request.CandidateAgents.FirstOrDefault(a => a.Id == result.Agent.Id);
            if (candidate is null)
            {
                _logger.LogWarning(
                    "Agent selector {SelectorType} (selector ID {SelectorId}) returned agent {AgentId}, which is not " +
                    "a candidate for surface {SurfaceId}. Ignoring it.",
                    selector.GetType(),
                    result.SelectorId,
                    result.Agent.Id,
                    request.SurfaceId);
                continue;
            }

            // Always return the candidate instance, not whatever object the selector handed back -
            // a selector should only ever be deciding *which* agent, never supplying its own copy.
            return result with { Agent = candidate };
        }

        return null;
    }

    /// <summary>
    /// Resolves <paramref name="previousAgentId"/> against the candidates. An ID that isn't a
    /// candidate (not sent, not a GUID the browser could parse, or no longer allowed) becomes
    /// <c>null</c>.
    /// </summary>
    private static AIAgent? ResolvePreviousAgent(Guid? previousAgentId, IReadOnlyList<AIAgent> candidates)
        => previousAgentId is { } id ? candidates.FirstOrDefault(a => a.Id == id) : null;

    /// <summary>
    /// Gets the current user's user group IDs. Empty list if there is no current user.
    /// </summary>
    private Task<IReadOnlyList<Guid>> GetCurrentUserGroupIdsAsync(CancellationToken cancellationToken)
    {
        var user = _backOfficeSecurityAccessor?.BackOfficeSecurity?.CurrentUser;
        if (user is null)
        {
            return Task.FromResult<IReadOnlyList<Guid>>([]);
        }

        var groupIds = user.Groups.Select(g => g.Key).ToList();
        return Task.FromResult<IReadOnlyList<Guid>>(groupIds);
    }
}
