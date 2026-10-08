using Umbraco.AI.Agent.Conversations.Core.Conversations;
using Umbraco.AI.Agent.Copilot.Workspace.Core.Surfaces;
using Umbraco.AI.Agent.Copilot.Workspace.Web.Agents;
using Umbraco.AI.Agent.Core.Agents;
using Umbraco.AI.Agent.Core.Surfaces;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbraco.AI.Agent.Copilot.Workspace.Web.NotificationHandlers;

/// <summary>
/// Cancels saving a conversation whose stored agent can't run in Copilot Workspace: the named agent is
/// missing, inactive, or not opted in to the <c>copilot-workspace</c> surface. Null, empty and
/// <c>"auto"</c> are always allowed.
/// </summary>
/// <remarks>
/// <para>
/// Lives on the Workspace side on purpose: the Conversations assemblies are host-agnostic and must not
/// know which surface hosts them.
/// </para>
/// <para>
/// Only a <em>change</em> of agent is checked. When an existing conversation is saved with the agent it
/// already has (a rename, a pin, a message truncate on regenerate), the save goes through even if that
/// agent has since become unavailable - the stream endpoint already falls back to auto-selection for it,
/// so blocking the save would only stop the user tidying up an otherwise working conversation.
/// </para>
/// </remarks>
internal sealed class CopilotWorkspaceConversationAgentSavingNotificationHandler
    : INotificationAsyncHandler<AIConversationSavingNotification>
{
    private readonly IAIConversationService _conversationService;
    private readonly IAIAgentService _agentService;
    private readonly AIAgentScopeValidator _scopeValidator;
    private readonly AIAgentSurfaceCollection _surfaces;

    /// <summary>
    /// Initializes a new instance of the <see cref="CopilotWorkspaceConversationAgentSavingNotificationHandler"/> class.
    /// </summary>
    public CopilotWorkspaceConversationAgentSavingNotificationHandler(
        IAIConversationService conversationService,
        IAIAgentService agentService,
        AIAgentScopeValidator scopeValidator,
        AIAgentSurfaceCollection surfaces)
    {
        _conversationService = conversationService;
        _agentService = agentService;
        _scopeValidator = scopeValidator;
        _surfaces = surfaces;
    }

    /// <inheritdoc />
    public async Task HandleAsync(AIConversationSavingNotification notification, CancellationToken cancellationToken)
    {
        var agentIdOrAlias = notification.Entity.AgentIdOrAlias;
        if (CopilotWorkspaceAgentAvailability.IsAuto(agentIdOrAlias))
        {
            return;
        }

        // Unchanged agent on an existing conversation: let it through (see remarks).
        var existing = await _conversationService.GetConversationAsync(notification.Entity.Id, cancellationToken);
        if (existing is not null
            && string.Equals(existing.AgentIdOrAlias, agentIdOrAlias, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var agent = await CopilotWorkspaceAgentAvailability.FindAgentAsync(_agentService, agentIdOrAlias!, cancellationToken);
        if (agent is not null && CopilotWorkspaceAgentAvailability.IsAvailable(agent, _scopeValidator, _surfaces))
        {
            return;
        }

        notification.Messages.Add(new EventMessage(
            "Agent not available",
            $"Agent '{agentIdOrAlias}' is not available in the '{CopilotWorkspaceAgentSurface.SurfaceId}' surface. "
                + "It may not exist, may be inactive, or may not be enabled for Copilot Workspace.",
            EventMessageType.Error));
        notification.Cancel = true;
    }
}
