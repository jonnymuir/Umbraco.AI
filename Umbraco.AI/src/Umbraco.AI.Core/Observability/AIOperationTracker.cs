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
            var result = await operation(cancellationToken);
            await scope.CompleteAsync(result.Usage, result.AuditResponse);
            return result;
        }
        catch (Exception ex)
        {
            await scope.FailAsync(ex);
            throw;
        }
        finally
        {
            scope.Dispose();
        }
    }

    public async Task<AIOperationScope> BeginAsync(AIOperationDescriptor descriptor, CancellationToken cancellationToken)
    {
        AIAuditScope? auditScope = null;
        AIAuditLog? auditLog = null;
        AIAuditPrompt? auditPrompt = null;

        if (_auditLogOptions.CurrentValue.Enabled && _contextAccessor.Context is not null)
        {
            var auditContext = AIAuditContext.ExtractFromRuntimeContext(
                descriptor.Capability, _contextAccessor.Context, descriptor.PromptData);

            auditLog = _auditLogFactory.Create(auditContext, descriptor.Metadata, parentId: AIAuditScope.Current?.AuditLogId);
            auditScope = AIAuditScope.Begin(auditLog.Id);
            auditLog.TraceId = Activity.Current?.TraceId.ToString();

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
            this, descriptor, auditScope, auditLog, auditPrompt, usageContext, cancellationToken);
    }

    /// <summary>
    /// Reports a finished call to the ambient <see cref="AIUsageCollectionScope"/>, if one is open.
    /// Runs synchronously on the caller's flow (so the ambient collector and runtime context are the
    /// call's own), independent of the analytics toggle and <see cref="AIOperationDescriptor.RecordUsageWhenEmpty"/>.
    /// Uses the usage context captured at <see cref="BeginAsync"/>, not the live runtime context.
    /// Never throws into the AI call.
    /// </summary>
    internal void CollectUsage(AIOperationDescriptor descriptor, AIUsageContext? usageContext, UsageDetails? usage)
    {
        try
        {
            var collector = AIUsageCollectionScope.Current;
            if (collector is null)
            {
                return;
            }

            collector.RecordCall(
                descriptor.Capability,
                usageContext?.ProviderId,
                usageContext?.ModelId,
                usageContext?.ProfileId == Guid.Empty ? null : usageContext?.ProfileId,
                usageContext?.ProfileAlias,
                usageContext?.FeatureType,
                usageContext?.FeatureId == Guid.Empty ? null : usageContext?.FeatureId,
                usageContext?.FeatureAlias,
                usage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect AI usage for {Capability}", descriptor.Capability);
        }
    }

    internal async Task RecordUsageAsync(
        AIOperationDescriptor descriptor, AIUsageContext? usageContext, UsageDetails? usage, long durationMs,
        bool succeeded, string? errorMessage, CancellationToken cancellationToken)
    {
        try
        {
            if (!_analyticsOptions.CurrentValue.Enabled || usageContext is null)
            {
                return;
            }

            if (usage is null && !descriptor.RecordUsageWhenEmpty)
            {
                return; // chat/embedding: no token counts => nothing to record
            }

            var recordContext = AIUsageRecordContext.FromUsageContext(usageContext);
            var result = new AIUsageRecordResult
            {
                Usage = usage,
                DurationMs = durationMs,
                Succeeded = succeeded,
                ErrorMessage = errorMessage,
            };

            var record = _usageRecordFactory.Create(recordContext, result);
            await _usageRecordingService.QueueRecordUsageAsync(record, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record AI usage for {Capability}", descriptor.Capability);
        }
    }
}
