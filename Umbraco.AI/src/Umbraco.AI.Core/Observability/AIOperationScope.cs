using System.Diagnostics;
using Microsoft.Extensions.AI;
using Umbraco.AI.Core.AuditLog;

namespace Umbraco.AI.Core.Observability;

/// <summary>
/// A tracking scope for a single AI operation. Created by <see cref="AIOperationTracker.BeginAsync"/>.
/// Completing or failing the scope measures the outcome once and hands it to each recording in
/// recorder order.
/// </summary>
internal sealed class AIOperationScope
{
    private readonly AIOperationTracker _tracker;
    private readonly IReadOnlyList<IAIOperationRecording> _recordings;
    private readonly Stopwatch _stopwatch;

    internal AIOperationScope(AIOperationTracker tracker, IReadOnlyList<IAIOperationRecording> recordings)
    {
        _tracker = tracker;
        _recordings = recordings;
        _stopwatch = Stopwatch.StartNew();
    }

    public Task CompleteAsync(UsageDetails? usage, AIAuditResponse? auditResponse)
    {
        _stopwatch.Stop();
        return _tracker.EndRecordingsAsync(
            _recordings,
            new AIOperationOutcome(AIOperationStatus.Succeeded, usage, _stopwatch.ElapsedMilliseconds, Exception: null, auditResponse));
    }

    public Task FailAsync(Exception exception, UsageDetails? usage = null)
    {
        _stopwatch.Stop();
        return _tracker.EndRecordingsAsync(
            _recordings,
            new AIOperationOutcome(AIOperationStatus.Failed, usage, _stopwatch.ElapsedMilliseconds, exception));
    }

    /// <summary>
    /// Opens each recording's ambient scope (today, the audit parent for nested calls) and closes them all,
    /// in reverse order, on dispose. Returns null when no recording needs one.
    /// </summary>
    /// <remarks>
    /// Enter it in the caller's own frame, directly around the work (for a stream, around each step of the
    /// inner enumerator): AsyncLocal changes made inside an async method or iterator do not survive its
    /// return or a yield.
    /// </remarks>
    public IDisposable? EnterScope()
    {
        List<IDisposable>? scopes = null;
        foreach (var recording in _recordings)
        {
            if (recording.EnterScope() is { } scope)
            {
                (scopes ??= []).Add(scope);
            }
        }

        return scopes switch
        {
            null => null,
            [var only] => only,
            _ => new CompositeScope(scopes),
        };
    }

    private sealed class CompositeScope(List<IDisposable> scopes) : IDisposable
    {
        public void Dispose()
        {
            for (var i = scopes.Count - 1; i >= 0; i--)
            {
                scopes[i].Dispose();
            }
        }
    }
}
