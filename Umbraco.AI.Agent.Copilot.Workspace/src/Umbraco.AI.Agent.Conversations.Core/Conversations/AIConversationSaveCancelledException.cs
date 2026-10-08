namespace Umbraco.AI.Agent.Conversations.Core.Conversations;

/// <summary>
/// Thrown when an <see cref="AIConversationSavingNotification"/> handler cancels a conversation save.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> and keeps the existing message, so callers that
/// already catch that keep working. Its own type lets the web layer tell "a handler said no" apart from
/// "conversation not found" and answer with a 400 rather than a 404 or 500. The web layer only learns
/// that the save was cancelled and the reason text the handler gave - never which host or rule refused it.
/// </remarks>
internal sealed class AIConversationSaveCancelledException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIConversationSaveCancelledException"/> class.
    /// </summary>
    /// <param name="reason">The reasons the cancelling handler(s) gave, joined into one line.</param>
    public AIConversationSaveCancelledException(string reason)
        : base($"Conversation save cancelled: {reason}")
        => Reason = reason;

    /// <summary>
    /// Gets the reasons the cancelling handler(s) gave, joined into one line.
    /// </summary>
    public string Reason { get; }
}
