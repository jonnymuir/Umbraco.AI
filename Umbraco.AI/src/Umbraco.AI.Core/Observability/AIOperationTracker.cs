using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.AI.Core.Analytics.Usage;
using Umbraco.AI.Core.AuditLog;
using Umbraco.AI.Core.AuditLog.Middleware;
using Umbraco.AI.Core.RuntimeContext;

namespace Umbraco.AI.Core.Observability;

/// <inheritdoc cref="IAIOperationTracker" />
internal sealed class AIOperationTracker : IAIOperationTracker
{
    private readonly IAIRuntimeContextAccessor _contextAccessor;
    private readonly IAIAuditLogFactory _auditLogFactory;
    private readonly IOptionsMonitor<AIAuditLogOptions> _auditLogOptions;
    private readonly IReadOnlyList<IAIOperationRecorder> _recorders;
    private readonly ILogger<AIOperationTracker> _logger;

    internal IAIAuditLogService AuditLogService { get; }

    public AIOperationTracker(
        IAIRuntimeContextAccessor contextAccessor,
        IAIAuditLogService auditLogService,
        IAIAuditLogFactory auditLogFactory,
        IOptionsMonitor<AIAuditLogOptions> auditLogOptions,
        IEnumerable<IAIOperationRecorder> recorders,
        ILogger<AIOperationTracker> logger)
    {
        _contextAccessor = contextAccessor;
        AuditLogService = auditLogService;
        _auditLogFactory = auditLogFactory;
        _auditLogOptions = auditLogOptions;
        _recorders = recorders.ToList();
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
        // semantic search embeddings) overwrite these keys before this call completes, so recorders
        // never re-read the live context.
        var identity = _contextAccessor.Context is { } context
            ? AIUsageContext.ExtractFromRuntimeContext(descriptor.Capability, context)
            : null;

        var recordings = await BeginRecordingsAsync(new AIOperationStart(descriptor, identity), cancellationToken);

        return new AIOperationScope(this, auditLog, auditPrompt, recordings);
    }

    private async Task<IReadOnlyList<IAIOperationRecording>> BeginRecordingsAsync(
        AIOperationStart start,
        CancellationToken cancellationToken)
    {
        var recordings = new List<IAIOperationRecording>(_recorders.Count);
        foreach (var recorder in _recorders)
        {
            try
            {
                if (await recorder.BeginAsync(start, cancellationToken) is { } recording)
                {
                    recordings.Add(recording);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Recorder} failed to start recording {Capability}",
                    recorder.GetType().FullName, start.Descriptor.Capability);
            }
        }

        return recordings;
    }

    /// <summary>
    /// Hands a finished call's outcome to each recording, in recorder order. Never throws into the AI call.
    /// </summary>
    internal async Task EndRecordingsAsync(IReadOnlyList<IAIOperationRecording> recordings, AIOperationOutcome outcome)
    {
        foreach (var recording in recordings)
        {
            try
            {
                await recording.EndAsync(outcome);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Recording} failed to record the end of an AI call", recording.GetType().FullName);
            }
        }
    }
}
