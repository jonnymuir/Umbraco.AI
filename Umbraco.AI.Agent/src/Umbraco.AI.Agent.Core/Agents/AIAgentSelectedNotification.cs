using Umbraco.AI.Agent.Core.Agents.Selection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbraco.AI.Agent.Core.Agents;

/// <summary>
/// Published after agent selection picks an agent for an <c>auto</c> request (not cancelable).
/// </summary>
/// <remarks>
/// Selected does not mean ran: <see cref="AIAgentExecutingNotification"/> fires afterwards and can
/// still cancel the run.
/// </remarks>
public sealed class AIAgentSelectedNotification : StatefulNotification
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIAgentSelectedNotification"/> class.
    /// </summary>
    /// <param name="selection">The selection result - the picked agent, selector ID and reason.</param>
    /// <param name="request">The request exactly as the selectors saw it.</param>
    /// <param name="messages">Event messages from the selection operation.</param>
    public AIAgentSelectedNotification(
        AIAgentSelectionResult selection,
        AIAgentSelectionRequest request,
        EventMessages messages)
    {
        Selection = selection;
        Request = request;
        Messages = messages;
    }

    /// <summary>
    /// Gets the selection result - the picked agent, selector ID and reason.
    /// </summary>
    public AIAgentSelectionResult Selection { get; }

    /// <summary>
    /// Gets the request exactly as the selectors saw it.
    /// </summary>
    public AIAgentSelectionRequest Request { get; }

    /// <summary>
    /// Gets the event messages.
    /// </summary>
    public EventMessages Messages { get; }
}
