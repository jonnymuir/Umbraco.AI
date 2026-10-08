using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.AI.Core.Analytics;
using Umbraco.AI.Core.Analytics.Usage;
using Umbraco.AI.Core.AuditLog;
using Umbraco.AI.Core.AuditLog.Middleware;
using Umbraco.AI.Core.RuntimeContext;

namespace Umbraco.AI.Core.Observability;

/// <inheritdoc cref="IAIOperationTracker" />
internal sealed class AIOperationTracker : IAIOperationTracker
{
    private readonly IAIRuntimeContextAccessor _contextAccessor;
    private readonly IAIUsageRecordingService _usageRecordingService;
    private readonly IAIUsageRecordFactory _usageRecordFactory;
    private readonly IOptionsMonitor<AIAnalyticsOptions> _analyticsOptions;
    private readonly IAIAuditLogFactory _auditLogFactory;
    private readonly IOptionsMonitor<AIAuditLogOptions> _auditLogOptions;
    private readonly ILogger<AIOperationTracker> _logger;

    internal IAIAuditLogService AuditLogService { get; }

    public AIOperationTracker(
        IAIRuntimeContextAccessor contextAccessor,
        IAIAuditLogService auditLogService,
        IAIAuditLogFactory auditLogFactory,
        IOptionsMonitor<AIAuditLogOptions> auditLogOptions,
        IAIUsageRecordingService usageRecordingService,
        IAIUsageRecordFactory usageRecordFactory,
        IOptionsMonitor<AIAnalyticsOptions> analyticsOptions,
        ILogger<AIOperationTracker> logger)
    {
        _contextAccessor = contextAccessor;
        AuditLogService = auditLogService;
        _auditLogFactory = auditLogFactory;
        _auditLogOptions = auditLogOptions;
        _usageRecordingService = usageRecordingService;
        _usageRecordFactory = usageRecordFactory;
        _analyticsOptions = analyticsOptions;
        _logger = logger;
    }

    public async Task<AITrackedOperationResult<TResult>> TrackAsync<TResult>(
        AIOperationDescriptor descriptor,
        Func<CancellationToken, Task<AITrackedOperationResult<TResult>>> operation,
        CancellationToken cancellationToken)
    {
        var scope = await BeginAsync(descriptor, cancellationToken);
        try
        {
            AITrackedOperationResult<TResult> result;
            using (scope.EnterAuditScope())
            {
                result = await operation(cancellationToken);
            }

            await scope.CompleteAsync(result.Usage, result.AuditResponse);
            return result;
        }
        catch (Exception ex)
        {
            await scope.FailAsync(ex);
            throw;
        }
    }

    public async Task<AIOperationScope> BeginAsync(AIOperationDescriptor descriptor, CancellationToken cancellationToken)
    {
        AIAuditLog? auditLog = null;
        AIAuditPrompt? auditPrompt = null;

        if (_auditLogOptions.CurrentValue.Enabled && _contextAccessor.Context is not null)
        {
            var auditContext = AIAuditContext.ExtractFromRuntimeContext(
                descriptor.Capability, _contextAccessor.Context, descriptor.PromptData);

            auditLog = _auditLogFactory.Create(auditContext, descriptor.Metadata, parentId: AIAuditScope.Current?.AuditLogId);
            auditLog.TraceId = Activity.Current?.TraceId.ToString();

            // This call's own AIAuditScope is not begun here. AsyncLocal changes made inside an async method
            // are discarded when it returns, so the caller enters it via AIOperationScope.EnterAuditScope
            // around the actual AI call, where nested calls can see it.
            await AuditLogService.QueueStartAuditLogAsync(auditLog, ct: cancellationToken);

            auditPrompt = new AIAuditPrompt { Data = descriptor.PromptData, Capability = descriptor.Capability };
        }

        // Enrich ambient Activity regardless of audit toggle (falls back to runtime context).
        AIActivityEnricher.EnrichCurrentActivity(auditLog, _contextAccessor);

        // Captured now, while the context still belongs to this call: nested AI calls (guardrail judge,
        // semantic search embeddings) overwrite these keys before this call completes. Serves both
        // usage analytics and test usage collection, so completion never re-reads the live context.
        var usageContext = _contextAccessor.Context is { } context
            ? AIUsageContext.ExtractFromRuntimeContext(descriptor.Capability, context)
            : null;

        return new AIOperationScope(
            this, descriptor, auditLog, auditPrompt, usageContext, cancellationToken);
    }

    /// <summary>
    /// The single usage path for a finished call: every tracked call's usage is captured once into an
    /// <see cref="AIUsageObservation"/> and handed to each consumer, which applies its own rules.
    /// Never throws into the AI call.
    /// </summary>
    internal void ReportUsage(AIUsageObservation observation, CancellationToken cancellationToken)
    {
        CollectUsage(observation);
        _ = RecordUsageAsync(observation, cancellationToken);
    }

    /// <summary>
    /// Adds the call to the ambient <see cref="AIUsageCollectionScope"/>, if one is open.
    /// Runs synchronously on the caller's flow, because it reads the ambient collector from it,
    /// independent of the analytics toggle and <see cref="AIOperationDescriptor.RecordUsageWhenEmpty"/>.
    /// </summary>
    private void CollectUsage(AIUsageObservation observation)
    {
        try
        {
            var collector = AIUsageCollectionScope.Current;
            if (collector is null)
            {
                return;
            }

            var usageContext = observation.Context;
            collector.RecordCall(
                observation.Descriptor.Capability,
                usageContext?.ProviderId,
                usageContext?.ModelId,
                // GetValue<Guid> returns Guid.Empty for a missing key; normalised here rather than in
                // AIUsageContext.ExtractFromRuntimeContext so persisted analytics values don't change.
                usageContext?.ProfileId == Guid.Empty ? null : usageContext?.ProfileId,
                usageContext?.ProfileAlias,
                usageContext?.FeatureType,
                usageContext?.FeatureId == Guid.Empty ? null : usageContext?.FeatureId,
                usageContext?.FeatureAlias,
                observation.Usage,
                observation.DurationMs,
                observation.Succeeded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect AI usage for {Capability}", observation.Descriptor.Capability);
        }
    }

    /// <summary>
    /// Persists the call to usage analytics, when analytics is enabled and the call has something to record.
    /// </summary>
    private async Task RecordUsageAsync(AIUsageObservation observation, CancellationToken cancellationToken)
    {
        try
        {
            if (!_analyticsOptions.CurrentValue.Enabled || observation.Context is null)
            {
                return;
            }

            if (observation.Usage is null && !observation.Descriptor.RecordUsageWhenEmpty)
            {
                return; // chat/embedding: no token counts => nothing to record
            }

            var recordContext = AIUsageRecordContext.FromUsageContext(observation.Context);
            var result = new AIUsageRecordResult
            {
                Usage = observation.Usage,
                DurationMs = observation.DurationMs,
                Succeeded = observation.Succeeded,
                ErrorMessage = observation.ErrorMessage,
            };

            var record = _usageRecordFactory.Create(recordContext, result);
            await _usageRecordingService.QueueRecordUsageAsync(record, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record AI usage for {Capability}", observation.Descriptor.Capability);
        }
    }
}
