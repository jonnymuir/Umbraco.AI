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
/// Dispose ends the ambient <see cref="AIAuditScope"/>.
/// </summary>
internal sealed class AIOperationScope : IDisposable
{
    private readonly AIOperationTracker _tracker;
    private readonly AIOperationDescriptor _descriptor;
    private readonly AIUsageContext? _usageContext;
    private readonly AIAuditScope? _auditScope;
    private readonly AIAuditLog? _auditLog;
    private readonly AIAuditPrompt? _auditPrompt;
    private readonly Stopwatch _stopwatch;
    private readonly CancellationToken _cancellationToken;

    internal AIOperationScope(
        AIOperationTracker tracker,
        AIOperationDescriptor descriptor,
        AIAuditScope? auditScope,
        AIAuditLog? auditLog,
        AIAuditPrompt? auditPrompt,
        AIUsageContext? usageContext,
        CancellationToken cancellationToken)
    {
        _tracker = tracker;
        _descriptor = descriptor;
        _usageContext = usageContext;
        _auditScope = auditScope;
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

    public void Dispose() => _auditScope?.Dispose();
}
