namespace Umbraco.AI.Agent.Core.Agents.Selection;

/// <summary>
/// Decides which agent should handle an <c>auto</c> request: filters the surface's agents down to
/// active, scope-available candidates, then runs the registered <see cref="IAIAgentSelector"/> chain
/// over them.
/// </summary>
/// <remarks>
/// Owns the orchestration so <c>AIAgentService</c> (already large) doesn't grow further. See
/// <c>IAIAgentService.SelectAgentForPromptAsync</c> for the obsolete method this replaces.
/// </remarks>
public interface IAIAgentSelectionService
{
    /// <summary>
    /// Selects an agent for an <c>auto</c> request.
    /// </summary>
    /// <param name="input">Everything a caller has before candidate filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The selected agent, selector ID and reason. <c>null</c> only when there are no active,
    /// scope-available candidates for <paramref name="input"/>'s surface and context.
    /// </returns>
    Task<AIAgentSelectionResult?> SelectAgentAsync(
        AIAgentSelectionInput input,
        CancellationToken cancellationToken = default);
}
