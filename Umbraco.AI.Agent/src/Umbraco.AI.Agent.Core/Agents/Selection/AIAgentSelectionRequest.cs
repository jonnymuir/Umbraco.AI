using Microsoft.Extensions.AI;
using Umbraco.AI.Core.RuntimeContext;

namespace Umbraco.AI.Agent.Core.Agents.Selection;

/// <summary>
/// Everything a selector sees when deciding which agent should handle an <c>auto</c> request.
/// </summary>
/// <remarks>
/// Built by the agent selection service from an <see cref="AIAgentSelectionInput"/>, after candidate
/// filtering (active + scope) has already run. Selectors may only return one of
/// <see cref="CandidateAgents"/> - anything else is treated as "no opinion".
/// </remarks>
public sealed class AIAgentSelectionRequest
{
    /// <summary>
    /// Active, scope-available agents, in the same order the surface returned them. Selectors may
    /// only return one of these.
    /// </summary>
    public required IReadOnlyList<AIAgent> CandidateAgents { get; init; }

    /// <summary>
    /// The full conversation as Microsoft.Extensions.AI messages (history + attachments), converted once.
    /// </summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>
    /// The surface / section / entity type the request came from - what scope rules use.
    /// </summary>
    public required AgentAvailabilityContext AvailabilityContext { get; init; }

    /// <summary>
    /// Raw request context items (entity key, content type, etc.) - everything the frontend sent, not
    /// just the fields <see cref="AvailabilityContext"/> extracts. This is what lets a selector see
    /// "the context" without us guessing which fields it needs.
    /// </summary>
    public required IReadOnlyList<AIRequestContextItem> ContextItems { get; init; }

    /// <summary>
    /// The surface the request was made from.
    /// </summary>
    public required string SurfaceId { get; init; }

    /// <summary>
    /// The current user's group IDs, resolved once for the chain.
    /// </summary>
    public required IReadOnlyList<Guid> UserGroupIds { get; init; }

    /// <summary>
    /// Tools the frontend offered for this request.
    /// </summary>
    public IReadOnlyList<AIFrontendTool> FrontendTools { get; init; } = [];

    /// <summary>
    /// The agent picked on the previous turn, resolved against <see cref="CandidateAgents"/>. Null if
    /// the browser sent nothing, sent garbage, or the agent is no longer a candidate.
    /// </summary>
    public AIAgent? PreviousAgent { get; init; }
}
