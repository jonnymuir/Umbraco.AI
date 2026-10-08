using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Umbraco.AI.Core.AuditLog;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Observability;
using Umbraco.AI.Core.RuntimeContext;

namespace Umbraco.AI.Core.Chat.Middleware;

/// <summary>
/// Chat client that records usage analytics and audit entries around a chat completion, by
/// delegating to the shared <see cref="IAIOperationTracker"/>. Replaces the former separate
/// tracking/usage-recording/auditing chat client trio with a single tracker-backed client.
/// </summary>
internal sealed class AITrackingChatClient : AIBoundChatClientBase
{
    private readonly IAIOperationTracker _tracker;
    private readonly IAIRuntimeContextAccessor _contextAccessor;

    public AITrackingChatClient(IChatClient innerClient, IAIOperationTracker tracker, IAIRuntimeContextAccessor contextAccessor)
        : base(innerClient)
    {
        _tracker = tracker;
        _contextAccessor = contextAccessor;
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var messages = chatMessages.ToList();
        var descriptor = BuildDescriptor(messages);

        var tracked = await _tracker.TrackAsync(
            descriptor,
            async token =>
            {
                var response = await base.GetResponseAsync(messages, options, token);
                return new AITrackedOperationResult<ChatResponse>
                {
                    Result = response,
                    Usage = response.Usage,
                    AuditResponse = new AIAuditResponse { Data = response.Messages, Usage = response.Usage },
                };
            },
            cancellationToken);

        return tracked.Result;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = chatMessages.ToList();
        var descriptor = BuildDescriptor(messages);

        var scope = await _tracker.BeginAsync(descriptor, cancellationToken);
        var updates = new List<ChatResponseUpdate>();
        Exception? captured = null;

        // yield cannot sit inside try/catch, so drive the enumerator manually (matches prior behavior).
        await using var enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            ChatResponseUpdate current;
            try
            {
                // Entered per step: the audit scope is AsyncLocal and doesn't survive this iterator's yields.
                using (scope.EnterAuditScope())
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }
                }

                current = enumerator.Current;
            }
            catch (Exception ex)
            {
                captured = ex;
                break;
            }

            updates.Add(current);
            yield return current;
        }

        if (captured is not null)
        {
            await scope.FailAsync(captured);
            throw captured;
        }

        var aggregated = updates.ToChatResponse();

        // Some providers report a failure (e.g. a rate limit hit on the final model call of a
        // tool loop) as streamed ErrorContent rather than by throwing, so the stream itself ends
        // normally. Record the call as failed when the response ends on such an error, keeping
        // the usage it consumed; an error the model carried on past stays a success.
        if (FindTerminalProviderError(aggregated) is { } providerError)
        {
            await scope.FailAsync(new AIStreamedProviderErrorException(providerError), aggregated.Usage);
        }
        else
        {
            await scope.CompleteAsync(
                aggregated.Usage,
                new AIAuditResponse { Data = aggregated.Messages, Usage = aggregated.Usage });
        }
    }

    /// <summary>
    /// Returns the provider error the response ended on: an <see cref="ErrorContent"/> in the last
    /// assistant message with no text or function call after it. Null when the response ended normally.
    /// </summary>
    private static ErrorContent? FindTerminalProviderError(ChatResponse response)
    {
        var lastAssistant = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        if (lastAssistant is null)
        {
            return null;
        }

        for (var i = lastAssistant.Contents.Count - 1; i >= 0; i--)
        {
            switch (lastAssistant.Contents[i])
            {
                case ErrorContent error:
                    return error;
                case TextContent text when !string.IsNullOrWhiteSpace(text.Text):
                case FunctionCallContent:
                    return null;
            }
        }

        return null;
    }

    private AIOperationDescriptor BuildDescriptor(IReadOnlyList<ChatMessage> messages) => new()
    {
        Capability = AICapability.Chat,
        PromptData = messages,
        Metadata = AIAuditMetadata.ExtractFromRuntimeContext(_contextAccessor.Context),
        RecordUsageWhenEmpty = true,
    };
}

/// <summary>
/// A provider failure reported as streamed <see cref="ErrorContent"/> rather than thrown, wrapped so
/// the audit log can record it like any other failed call.
/// </summary>
internal sealed class AIStreamedProviderErrorException(ErrorContent error)
    : Exception(string.IsNullOrEmpty(error.ErrorCode)
        ? error.Message ?? "The provider returned an error."
        : $"{error.ErrorCode}: {error.Message ?? "The provider returned an error."}")
{
    /// <summary>
    /// Gets the provider's error code, when it sent one.
    /// </summary>
    public string? ErrorCode { get; } = error.ErrorCode;
}
