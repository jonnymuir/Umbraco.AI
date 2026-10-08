using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.AI.Core.Chat.Middleware;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Observability;
using Umbraco.AI.Core.RuntimeContext;
using Umbraco.AI.Core.Telemetry;

namespace Umbraco.AI.Tests.Unit.Middleware;

/// <summary>
/// #562: a streamed chat call's gen_ai span carries the tracked call's tags, like a non-streamed one.
/// </summary>
public class AIOpenTelemetryChatMiddlewareTagsTests
{
    [Fact]
    public async Task StreamedCall_TagsTheGenAiSpan()
    {
        // Arrange
        Activity? span = null;
        var client = new AIOpenTelemetryChatMiddleware(NullLoggerFactory.Instance)
            .Apply(new StreamingClient(() => span = Activity.Current));
        var tracker = new AIOperationTracker(
            Mock.Of<IAIRuntimeContextAccessor>(),
            [new TaggingRecorder()],
            NullLogger<AIOperationTracker>.Instance);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AITelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        var scope = await tracker.BeginAsync(new AIOperationDescriptor { Capability = AICapability.Chat }, CancellationToken.None);

        // Act: as the tracking client does, enter the scope around each step of the stream.
        await using var updates = client.GetStreamingResponseAsync("hi").GetAsyncEnumerator();
        while (true)
        {
            using (scope.EnterScope())
            {
                if (!await updates.MoveNextAsync())
                {
                    break;
                }
            }
        }

        // Assert
        span.ShouldNotBeNull();
        span.Source.Name.ShouldBe(AITelemetry.SourceName);
        span.GetTagItem(AITelemetry.Tags.ProfileAlias).ShouldBe("streamed-profile");
    }

    [Fact]
    public async Task StreamedCall_StoppedEarly_DisposesTheProviderStream()
    {
        // Arrange
        var inner = new StreamingClient(() => { });
        var client = new AIOpenTelemetryChatMiddleware(NullLoggerFactory.Instance).Apply(inner);

        // Act: read one update, then stop without cancelling (e.g. a guardrail blocks mid-stream).
        await foreach (var _ in client.GetStreamingResponseAsync("hi"))
        {
            break;
        }

        // Assert
        inner.Disposed.ShouldBeTrue();
    }

    private sealed class TaggingRecorder : IAIOperationRecorder
    {
        public ValueTask<IAIOperationRecording?> BeginAsync(AIOperationStart start, CancellationToken cancellationToken)
        {
            start.ActivityTags[AITelemetry.Tags.ProfileAlias] = "streamed-profile";
            return ValueTask.FromResult<IAIOperationRecording?>(null);
        }
    }

    private sealed class StreamingClient(Action onCall) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            onCall();
            try
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
                yield return new ChatResponseUpdate(ChatRole.Assistant, "more");
            }
            finally
            {
                Disposed = true;
            }
        }

        public bool Disposed { get; private set; }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
