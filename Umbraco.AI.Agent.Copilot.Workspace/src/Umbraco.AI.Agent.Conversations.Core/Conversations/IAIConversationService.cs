namespace Umbraco.AI.Agent.Conversations.Core.Conversations;

/// <summary>
/// Service for conversation and message operations. All operations are scoped to the acting backoffice
/// user (resolved from the ambient backoffice security context): reads only return the caller's own
/// conversations, and writes are rejected for conversations the caller does not own. This is the
/// server-side ownership boundary behind the section-access authorization (F-SEC / B7).
/// </summary>
public interface IAIConversationService
{
    /// <summary>Gets one of the acting user's conversations by id, or null if missing or not owned.</summary>
    Task<AIConversation?> GetConversationAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Gets a paged list of the acting user's conversations, newest activity first.</summary>
    Task<(IReadOnlyList<AIConversation> Items, int Total)> GetConversationsPagedAsync(
        int skip,
        int take,
        Guid? projectId = null,
        string? search = null,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if the acting user has any conversation (archived included) in the given project.
    /// Used to block deletion of a project that still owns conversations.
    /// </summary>
    Task<bool> ConversationsExistInProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Creates a conversation owned by the acting user.</summary>
    Task<AIConversation> CreateConversationAsync(AIConversation conversation, CancellationToken cancellationToken = default);

    /// <summary>Updates one of the acting user's conversations (title, pin, archive, project, profile, agent).</summary>
    Task UpdateConversationAsync(AIConversation conversation, CancellationToken cancellationToken = default);

    /// <summary>Deletes one of the acting user's conversations and purges its uploaded files.</summary>
    Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Gets a page of a conversation's messages in sequence order (ownership-checked).</summary>
    Task<(IReadOnlyList<AIMessage> Items, int Total)> GetMessagesPagedAsync(
        Guid conversationId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the text of the conversation's most recent user message, or null when it has none
    /// (ownership-checked). Used to prompt agent auto-selection on a run that carries no inbound user
    /// message of its own.
    /// </summary>
    Task<string?> GetLastUserMessageTextAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the agent ID of the conversation's newest assistant message, or null when it has none
    /// (ownership-checked). This is Copilot Workspace's previous pick for auto agent selection.
    /// </summary>
    /// <remarks>
    /// Added after this interface first shipped, so it has a default body to keep existing
    /// implementations compiling. The default pages backwards through
    /// <see cref="GetMessagesPagedAsync"/> (which does the ownership check) until it finds an
    /// assistant message. Implementations should override it with a single targeted query.
    /// </remarks>
    async Task<Guid?> GetLastAssistantAgentIdAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        const int pageSize = 50;

        var (_, total) = await GetMessagesPagedAsync(conversationId, 0, 1, cancellationToken);

        for (var end = total; end > 0; end -= pageSize)
        {
            var skip = Math.Max(0, end - pageSize);
            var (items, _) = await GetMessagesPagedAsync(conversationId, skip, end - skip, cancellationToken);

            var newestAssistant = items.LastOrDefault(
                m => string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase));
            if (newestAssistant is not null)
            {
                return newestAssistant.AgentId;
            }
        }

        return null;
    }

    /// <summary>
    /// Drops everything after the conversation's last user message (ownership-checked), so the next run
    /// answers that turn afresh instead of appending a second reply. This is the server-side half of the
    /// chat's regenerate action: the client truncates its own thread to match and then runs normally, so
    /// the AG-UI stream stays untouched. Returns the number of messages removed.
    /// </summary>
    Task<int> TruncateAfterLastUserMessageAsync(Guid conversationId, CancellationToken cancellationToken = default);
}
