using Microsoft.Extensions.AI;
using Umbraco.AI.Core.RuntimeContext;

namespace Umbraco.AI.Core.Chat.Middleware;

/// <summary>
/// Chat middleware that adds the profile's system prompt (<see cref="Profiles.AIChatProfileSettings.SystemPromptTemplate"/>)
/// as the first system message of every chat call made through that profile.
/// </summary>
/// <remarks>
/// <para>
/// The prompt is read from <see cref="Constants.ContextKeys.ProfileSystemPrompt"/>, which the
/// profile-scoped chat client sets per call. It applies to every caller of the profile: inline
/// chat, agents, prompts, and any LLM-backed guardrail or grader that uses it.
/// </para>
/// <para>
/// <strong>Ordering matters for provider prompt caching.</strong> Providers cache by request
/// prefix, so the profile prompt (fixed per profile) is placed first, ahead of anything that
/// changes per request: the runtime context parts (user, section, current entity) that agents and
/// prompts add, and the context resources merged in by <c>AIContextInjectingChatMiddleware</c>
/// (which merges into the first system message, i.e. this one). This is why the middleware is
/// registered outermost. Moving it inward, or appending the prompt after other messages, would
/// put per-request text ahead of it and break the cached prefix.
/// </para>
/// <para>
/// The text is sent as-is; template variables are not processed. Remove this middleware with
/// <c>builder.AIChatMiddleware().Remove&lt;AIProfileSystemPromptChatMiddleware&gt;()</c> to turn it off,
/// or replace it to change how the prompt is applied.
/// </para>
/// </remarks>
public sealed class AIProfileSystemPromptChatMiddleware : IAIChatMiddleware
{
    private readonly IAIRuntimeContextAccessor _contextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIProfileSystemPromptChatMiddleware"/> class.
    /// </summary>
    public AIProfileSystemPromptChatMiddleware(IAIRuntimeContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    /// <inheritdoc />
    public IChatClient Apply(IChatClient client) => new AIProfileSystemPromptChatClient(client, _contextAccessor);
}

/// <summary>
/// Chat client decorator that prepends the profile's system prompt from the runtime context.
/// </summary>
internal sealed class AIProfileSystemPromptChatClient : DelegatingChatClient
{
    private readonly IAIRuntimeContextAccessor _contextAccessor;

    public AIProfileSystemPromptChatClient(IChatClient innerClient, IAIRuntimeContextAccessor contextAccessor)
        : base(innerClient)
    {
        _contextAccessor = contextAccessor;
    }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetResponseAsync(ApplySystemPrompt(messages), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetStreamingResponseAsync(ApplySystemPrompt(messages), options, cancellationToken);

    private IEnumerable<ChatMessage> ApplySystemPrompt(IEnumerable<ChatMessage> messages)
    {
        var systemPrompt = _contextAccessor.Context?.GetValue<string>(Constants.ContextKeys.ProfileSystemPrompt);
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            return messages;
        }

        // New list so the caller's message list is never changed (agents and prompts reuse theirs
        // across calls, which would otherwise stack a copy of the prompt per call).
        return [new ChatMessage(ChatRole.System, systemPrompt), .. messages];
    }
}
