using Microsoft.Extensions.AI;
using Umbraco.AI.Core.RuntimeContext;

namespace Umbraco.AI.Agent.Core.Agents.Selection;

/// <summary>
/// What a caller has before filtering: the surface, availability context, conversation, raw context
/// items, frontend tools, and an optional previous-turn agent ID.
/// </summary>
/// <remarks>
/// The agent selection service turns this into an <see cref="AIAgentSelectionRequest"/> by looking up
/// the surface's agents, filtering to active + scope-available candidates, resolving the current
/// user's group IDs, and resolving <see cref="PreviousAgentId"/> against the candidates.
/// </remarks>
public sealed class AIAgentSelectionInput
{
    /// <summary>
    /// The surface the request was made from.
    /// </summary>
    public required string SurfaceId { get; init; }

    /// <summary>
    /// The surface / section / entity type the request came from - what scope rules use.
    /// </summary>
    public required AgentAvailabilityContext AvailabilityContext { get; init; }

    /// <summary>
    /// The full conversation as Microsoft.Extensions.AI messages (history + attachments).
    /// </summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>
    /// Raw request context items (entity key, content type, etc.) - everything the frontend sent.
    /// </summary>
    public required IReadOnlyList<AIRequestContextItem> ContextItems { get; init; }

    /// <summary>
    /// Tools the frontend offered for this request.
    /// </summary>
    public IReadOnlyList<AIFrontendTool> FrontendTools { get; init; } = [];

    /// <summary>
    /// The ID of the agent picked on the previous turn, as sent by the browser. Resolved against the
    /// candidates - an ID that isn't a candidate becomes <c>null</c> on the resulting request.
    /// </summary>
    public Guid? PreviousAgentId { get; init; }
}
