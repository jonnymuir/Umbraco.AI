using Microsoft.Extensions.AI;
using Umbraco.AI.Core.Analytics.Usage;

namespace Umbraco.AI.Core.Observability;

/// <summary>
/// The usage of one finished AI call, captured once by <see cref="AIOperationScope"/> and handed to every
/// usage consumer (usage analytics and the ambient <see cref="AIUsageCollectionScope"/>). Capture is
/// unconditional; each consumer applies its own rules about what to keep.
/// </summary>
/// <param name="Descriptor">The descriptor the operation was begun with.</param>
/// <param name="Context">The usage context captured at <see cref="AIOperationTracker.BeginAsync"/>, not the live runtime context.</param>
/// <param name="Usage">Token usage reported by the provider, if any.</param>
/// <param name="DurationMs">Wall-clock duration of the call.</param>
/// <param name="Succeeded">Whether the call completed without throwing.</param>
/// <param name="ErrorMessage">The failure message, when the call failed.</param>
internal sealed record AIUsageObservation(
    AIOperationDescriptor Descriptor,
    AIUsageContext? Context,
    UsageDetails? Usage,
    long DurationMs,
    bool Succeeded,
    string? ErrorMessage);
