namespace Umbraco.AI.Agent.Core.Agents.Selection;

/// <summary>
/// An opt-in selector that keeps the previous turn's agent for the rest of the conversation.
/// </summary>
/// <remarks>
/// <para>
/// Ships with the package but is **not** registered by default - turning it on keeps today's
/// re-pick-every-turn behaviour unchanged for everyone who doesn't ask for sticky selection.
/// Register it ahead of <see cref="LLMAgentSelector"/> (or any other selector that would switch
/// agents) so it gets first refusal:
/// </para>
/// <code>
/// builder.AIAgentSelectors().InsertBefore&lt;LLMAgentSelector, StickyAgentSelector&gt;();
/// </code>
/// </remarks>
public sealed class StickyAgentSelector : IAIAgentSelector
{
    /// <inheritdoc />
    public Task<AIAgentSelectionResult?> SelectAgentAsync(
        AIAgentSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = request.PreviousAgent is { } previousAgent
            ? new AIAgentSelectionResult(previousAgent, AIAgentSelectorIds.Sticky, Reason: null)
            : null;

        return Task.FromResult(result);
    }
}
