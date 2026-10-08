using Microsoft.Extensions.AI;
using Umbraco.AI.Core.AuditLog;

namespace Umbraco.AI.Core.Observability;

/// <summary>
/// How a tracked call ended, measured once by the tracker and handed to every <see cref="IAIOperationRecording"/>.
/// </summary>
/// <param name="Status">How the call ended.</param>
/// <param name="Usage">Token usage reported by the provider, if any. A failed call can still carry partial usage.</param>
/// <param name="DurationMs">Wall-clock duration of the call.</param>
/// <param name="Exception">The failure, when the call failed.</param>
/// <param name="Response">What the call returned, for the audit log. Null when the call failed.</param>
internal sealed record AIOperationOutcome(
    AIOperationStatus Status,
    UsageDetails? Usage,
    long DurationMs,
    Exception? Exception,
    AIAuditResponse? Response = null)
{
    /// <summary>Whether the call succeeded.</summary>
    public bool Succeeded => Status == AIOperationStatus.Succeeded;
}

/// <summary>
/// How a tracked call ended.
/// </summary>
internal enum AIOperationStatus
{
    /// <summary>The call completed.</summary>
    Succeeded,

    /// <summary>The call threw, or ended on a provider error.</summary>
    Failed,
}
