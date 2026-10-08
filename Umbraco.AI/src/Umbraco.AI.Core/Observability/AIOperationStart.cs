using Umbraco.AI.Core.Analytics.Usage;

namespace Umbraco.AI.Core.Observability;

/// <summary>
/// What the tracker knows about a call when it starts, handed to every <see cref="IAIOperationRecorder"/>.
/// </summary>
/// <param name="Descriptor">The descriptor the operation was begun with.</param>
/// <param name="Identity">
/// The call's profile, provider, model, entity and feature (with versions), read once from the runtime
/// context at the start rather than later: nested AI calls (guardrail judge, semantic search embeddings)
/// overwrite those keys before the outer call completes. Every recorder reads this, so the runtime context
/// is read in one place. Null when the call has no runtime context.
/// </param>
internal sealed record AIOperationStart(
    AIOperationDescriptor Descriptor,
    AIUsageContext? Identity);
