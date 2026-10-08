using System.Runtime.CompilerServices;
using Umbraco.AI.Agent.Core.Agents.Selection;
using Umbraco.AI.AGUI.Events;
using Umbraco.AI.AGUI.Events.Special;

namespace Umbraco.AI.Agent.Core.AGUI;

/// <summary>
/// Builds the <c>agent_selected</c> AG-UI custom event that tells the frontend which agent
/// <c>auto</c> selection picked, by which selector, and why.
/// </summary>
/// <remarks>
/// Every endpoint that streams an <c>auto</c> run (the Copilot agent stream and the Copilot
/// Workspace conversation stream) sends this event through here, so the payload shape is defined
/// once. <see cref="Name"/> is public so other code can recognise the event. Building and
/// prepending it is internal plumbing; the Workspace web project reaches it through
/// <c>InternalsVisibleTo</c>.
/// </remarks>
public static class AGUIAgentSelectedEvent
{
    /// <summary>
    /// The custom event name the frontend listens for.
    /// </summary>
    public const string Name = "agent_selected";

    /// <summary>
    /// Creates the <c>agent_selected</c> event for a selection outcome.
    /// </summary>
    /// <param name="selection">The selection outcome.</param>
    /// <returns>The custom event.</returns>
    internal static CustomEvent Create(AIAgentSelectionResult selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        var agent = selection.Agent;

        return new CustomEvent
        {
            Name = Name,
            Value = new
            {
                agentId = agent.Id,
                agentName = agent.Name,
                agentAlias = agent.Alias,
                selectorId = selection.SelectorId,
                reason = selection.Reason,
            },
        };
    }

    /// <summary>
    /// Returns the stream with the <c>agent_selected</c> event for <paramref name="selection"/> as
    /// its first event.
    /// </summary>
    /// <param name="stream">The AG-UI event stream of the run.</param>
    /// <param name="selection">The selection outcome.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stream with the event prepended.</returns>
    internal static async IAsyncEnumerable<IAGUIEvent> Prepend(
        IAsyncEnumerable<IAGUIEvent> stream,
        AIAgentSelectionResult selection,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return Create(selection);

        await foreach (var evt in stream.WithCancellation(cancellationToken))
        {
            yield return evt;
        }
    }
}
