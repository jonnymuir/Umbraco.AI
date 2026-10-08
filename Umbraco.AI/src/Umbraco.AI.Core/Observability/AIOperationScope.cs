using System.Diagnostics;
using Microsoft.Extensions.AI;
using Umbraco.AI.Core.AuditLog;

namespace Umbraco.AI.Core.Observability;

/// <summary>
/// A tracking scope for a single AI operation. Created by <see cref="AIOperationTracker.BeginAsync"/>.
/// Completing or failing the scope measures the outcome once, queues the audit status (awaited, on
/// <see cref="CancellationToken.None"/>), then hands the outcome to each recording in recorder order.
/// </summary>
internal sealed class AIOperationScope
{
    private readonly AIOperationTracker _tracker;
    private readonly AIAuditLog? _auditLog;
    private readonly AIAuditPrompt? _auditPrompt;
    private readonly IReadOnlyList<IAIOperationRecording> _recordings;
    private readonly Stopwatch _stopwatch;

    internal AIOperationScope(
        AIOperationTracker tracker,
        AIAuditLog? auditLog,
        AIAuditPrompt? auditPrompt,
        IReadOnlyList<IAIOperationRecording> recordings)
    {
        _tracker = tracker;
        _auditLog = auditLog;
        _auditPrompt = auditPrompt;
        _recordings = recordings;
        _stopwatch = Stopwatch.StartNew();
    }

    public async Task CompleteAsync(UsageDetails? usage, AIAuditResponse? auditResponse)
    {
        _stopwatch.Stop();
        var outcome = new AIOperationOutcome(AIOperationStatus.Succeeded, usage, _stopwatch.ElapsedMilliseconds, Exception: null);

        if (_auditLog is not null)
        {
            await _tracker.AuditLogService.QueueCompleteAuditLogAsync(
                _auditLog, _auditPrompt, auditResponse, CancellationToken.None);
        }

        await _tracker.EndRecordingsAsync(_recordings, outcome);
    }

    public async Task FailAsync(Exception exception, UsageDetails? usage = null)
    {
        _stopwatch.Stop();
        var outcome = new AIOperationOutcome(AIOperationStatus.Failed, usage, _stopwatch.ElapsedMilliseconds, exception);

        if (_auditLog is not null)
        {
            await _tracker.AuditLogService.QueueRecordAuditLogFailureAsync(
                _auditLog, _auditPrompt, exception, CancellationToken.None);
        }

        await _tracker.EndRecordingsAsync(_recordings, outcome);
    }

    /// <summary>
    /// Makes this call's audit entry the parent of any AI call made while the returned scope is open, and
    /// restores the previous parent on dispose. Returns null when the call has no audit entry.
    /// </summary>
    /// <remarks>
    /// Enter it in the caller's own frame, directly around the work (for a stream, around each step of the
    /// inner enumerator): <see cref="AIAuditScope"/> is AsyncLocal, and AsyncLocal changes made inside an
    /// async method or iterator do not survive its return or a yield.
    /// </remarks>
    public AIAuditScope? EnterAuditScope() => _auditLog is null ? null : AIAuditScope.Begin(_auditLog.Id);
}
