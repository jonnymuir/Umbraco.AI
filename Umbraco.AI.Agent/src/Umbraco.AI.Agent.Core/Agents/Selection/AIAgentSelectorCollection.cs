using Umbraco.Cms.Core.Composing;

namespace Umbraco.AI.Agent.Core.Agents.Selection;

/// <summary>
/// A collection of agent selectors executed in order to decide which agent handles an <c>auto</c> request.
/// </summary>
public sealed class AIAgentSelectorCollection : BuilderCollectionBase<IAIAgentSelector>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIAgentSelectorCollection"/> class.
    /// </summary>
    /// <param name="items">A factory function that returns the selector instances.</param>
    public AIAgentSelectorCollection(Func<IEnumerable<IAIAgentSelector>> items)
        : base(items)
    { }
}
