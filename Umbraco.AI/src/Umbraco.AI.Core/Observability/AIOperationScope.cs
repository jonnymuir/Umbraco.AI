using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.AI.Core.Analytics;
using Umbraco.AI.Core.Analytics.Usage;
using Umbraco.AI.Core.AuditLog;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.RuntimeContext;

namespace Umbraco.AI.Core.Observability;

/// <summary>
/// A tracking scope for a single AI operation. Created by <see cref="AIOperationTracker.BeginAsync"/>.
/// Completing or failing the scope reports the call's usage once (see <see cref="AIOperationTracker.ReportUsage"/>)
/// and queues the audit status (awaited, on <see cref="CancellationToken.None"/>).
/// </summary>
internal sealed class AIOperationScope
{
    private readonly AIOperationTracker _tracker;
    private readonly AIOperationDescriptor _descriptor;
    private readonly AIUsageContext? _usageContext;
    private readonly AIAuditLog? _auditLog;
    private readonly AIAuditPrompt? _auditPrompt;
    private readonly Stopwatch _stopwatch;
    private readonly CancellationToken _cancellationToken;

    internal AIOperationScope(
        AIOperationTracker tracker,
        AIOperationDescriptor descriptor,
        AIAuditLog? auditLog,
        AIAuditPrompt? auditPrompt,
        AIUsageContext? usageContext,
        CancellationToken cancellationToken)
    {
        _tracker = tracker;
        _descriptor = descriptor;
        _usageContext = usageContext;
        _auditLog = auditLog;
        _auditPrompt = auditPrompt;
        _cancellationToken = cancellationToken;
        _stopwatch = Stopwatch.StartNew();
    }

    public async Task CompleteAsync(UsageDetails? usage, AIAuditResponse? auditResponse)
    {
        _stopwatch.Stop();
        _tracker.ReportUsage(
            new AIUsageObservation(_descriptor, _usageContext, usage, _stopwatch.ElapsedMilliseconds, Succeeded: true, ErrorMessage: null),
            _cancellationToken);

        if (_auditLog is not null)
        {
            await _tracker.AuditLogService.QueueCompleteAuditLogAsync(
                _auditLog, _auditPrompt, auditResponse, CancellationToken.None);
        }
    }

    public async Task FailAsync(Exception exception, UsageDetails? usage = null)
    {
        _stopwatch.Stop();
        _tracker.ReportUsage(
            new AIUsageObservation(_descriptor, _usageContext, usage, _stopwatch.ElapsedMilliseconds, Succeeded: false, exception.Message),
            _cancellationToken);

        if (_auditLog is not null)
        {
            await _tracker.AuditLogService.QueueRecordAuditLogFailureAsync(
                _auditLog, _auditPrompt, exception, CancellationToken.None);
        }
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
