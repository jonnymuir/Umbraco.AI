using Umbraco.AI.Agent.Core.Agents.Selection;
using Umbraco.Cms.Core.DependencyInjection;

namespace Umbraco.AI.Agent.Extensions;

/// <summary>
/// Extension methods for <see cref="IUmbracoBuilder"/> for AI agent selector collection configuration.
/// </summary>
public static partial class UmbracoBuilderExtensions
{
    /// <summary>
    /// Gets the AI agent selector collection builder.
    /// </summary>
    /// <param name="builder">The Umbraco builder.</param>
    /// <returns>The AI agent selector collection builder.</returns>
    /// <remarks>
    /// Use this to configure the order agent selectors run in. Example:
    /// <code>
    /// builder.AIAgentSelectors()
    ///     .Append&lt;MySelector&gt;()
    ///     .InsertBefore&lt;LLMAgentSelector, MySelector&gt;();
    /// </code>
    /// </remarks>
    public static AIAgentSelectorCollectionBuilder AIAgentSelectors(this IUmbracoBuilder builder)
        => builder.WithCollectionBuilder<AIAgentSelectorCollectionBuilder>();
}
