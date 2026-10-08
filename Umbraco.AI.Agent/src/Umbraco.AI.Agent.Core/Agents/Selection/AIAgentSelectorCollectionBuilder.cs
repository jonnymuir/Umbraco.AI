using Umbraco.Cms.Core.Composing;

namespace Umbraco.AI.Agent.Core.Agents.Selection;

/// <summary>
/// An ordered collection builder for agent selectors.
/// </summary>
/// <remarks>
/// <para>
/// Use this builder to configure the order agent selectors run in:
/// </para>
/// <code>
/// builder.AIAgentSelectors()
///     .Append&lt;MySelector&gt;()
///     .InsertBefore&lt;LLMAgentSelector, MySelector&gt;();
/// </code>
/// </remarks>
public class AIAgentSelectorCollectionBuilder
    : OrderedCollectionBuilderBase<AIAgentSelectorCollectionBuilder, AIAgentSelectorCollection, IAIAgentSelector>
{
    /// <inheritdoc />
    protected override AIAgentSelectorCollectionBuilder This => this;
}
