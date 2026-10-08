using Microsoft.Extensions.AI;
using Umbraco.AI.Core;
using Umbraco.AI.Core.Chat;
using Umbraco.AI.Core.Chat.Middleware;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.RuntimeContext;
using Umbraco.AI.Tests.Common.Builders;
using Umbraco.AI.Tests.Common.Fakes;

namespace Umbraco.AI.Tests.Unit.Middleware;

public class AIProfileSystemPromptChatClientTests
{
    private readonly AIRuntimeContext _runtimeContext = new([]);
    private readonly Mock<IAIRuntimeContextAccessor> _contextAccessorMock = new();

    public AIProfileSystemPromptChatClientTests()
    {
        _contextAccessorMock.Setup(x => x.Context).Returns(_runtimeContext);
    }

    [Fact]
    public async Task GetResponseAsync_WithProfileSystemPrompt_PrependsSystemMessage()
    {
        _runtimeContext.SetValue(Constants.ContextKeys.ProfileSystemPrompt, "You write for Acme.");
        var inner = new FakeChatClient();
        var client = CreateClient(inner);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")]);

        inner.ReceivedMessages[0].Select(m => (m.Role, m.Text)).ShouldBe(
        [
            (ChatRole.System, "You write for Acme."),
            (ChatRole.User, "Hello"),
        ]);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithProfileSystemPrompt_PrependsSystemMessage()
    {
        _runtimeContext.SetValue(Constants.ContextKeys.ProfileSystemPrompt, "You write for Acme.");
        var inner = new FakeChatClient(["Hi"]);
        var client = CreateClient(inner);

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hello")]))
        {
        }

        inner.ReceivedMessages[0].First().Text.ShouldBe("You write for Acme.");
    }

    [Fact]
    public async Task GetResponseAsync_WithPerRequestSystemMessage_PutsProfilePromptFirst()
    {
        // The profile prompt is fixed text, so it must come before per-request system text
        // (user, section, entity context) to keep the provider's cached prefix stable.
        _runtimeContext.SetValue(Constants.ContextKeys.ProfileSystemPrompt, "You write for Acme.");
        var inner = new FakeChatClient();
        var client = CreateClient(inner);

        await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, "## Current User\nName: Jo"),
            new ChatMessage(ChatRole.User, "Hello"),
        ]);

        inner.ReceivedMessages[0].Select(m => m.Text).ShouldBe(
        [
            "You write for Acme.",
            "## Current User\nName: Jo",
            "Hello",
        ]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetResponseAsync_WithoutProfileSystemPrompt_PassesMessagesThrough(string? systemPrompt)
    {
        _runtimeContext.SetValue(Constants.ContextKeys.ProfileSystemPrompt, systemPrompt);
        var inner = new FakeChatClient();
        var client = CreateClient(inner);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")]);

        inner.ReceivedMessages[0].Select(m => m.Role).ShouldBe([ChatRole.User]);
    }

    [Fact]
    public async Task GetResponseAsync_CalledTwiceWithSameList_DoesNotChangeCallerList()
    {
        _runtimeContext.SetValue(Constants.ContextKeys.ProfileSystemPrompt, "You write for Acme.");
        var inner = new FakeChatClient();
        var client = CreateClient(inner);
        List<ChatMessage> messages = [new(ChatRole.User, "Hello")];

        await client.GetResponseAsync(messages);
        await client.GetResponseAsync(messages);

        messages.Count.ShouldBe(1);
        inner.ReceivedMessages[1].Count(m => m.Role == ChatRole.System).ShouldBe(1);
    }

    [Fact]
    public async Task GetResponseAsync_WithNoRuntimeContext_PassesMessagesThrough()
    {
        _contextAccessorMock.Setup(x => x.Context).Returns((AIRuntimeContext?)null);
        var inner = new FakeChatClient();
        var client = CreateClient(inner);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")]);

        inner.ReceivedMessages[0].Count().ShouldBe(1);
    }

    [Fact]
    public async Task ProfileClient_WithSystemPromptSetting_SendsItToTheModel()
    {
        var inner = new FakeChatClient();
        var client = CreateProfileClient(inner, systemPrompt: "You write for Acme.");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")]);

        inner.ReceivedMessages[0].First().Text.ShouldBe("You write for Acme.");
    }

    [Fact]
    public async Task ProfileClient_NextProfileHasNoSystemPrompt_DoesNotReusePreviousOne()
    {
        var inner = new FakeChatClient();
        var withPrompt = CreateProfileClient(inner, systemPrompt: "You write for Acme.");
        var withoutPrompt = CreateProfileClient(inner, systemPrompt: null);

        await withPrompt.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")]);
        await withoutPrompt.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")]);

        inner.ReceivedMessages[1].Select(m => m.Role).ShouldBe([ChatRole.User]);
    }

    private IChatClient CreateProfileClient(IChatClient inner, string? systemPrompt)
    {
        var profile = new AIProfileBuilder()
            .WithCapability(AICapability.Chat)
            .WithChatSettings(systemPromptTemplate: systemPrompt)
            .Build();

        return new ScopedProfileChatClient(
            CreateClient(inner),
            profile,
            _contextAccessorMock.Object,
            Mock.Of<IAIRuntimeContextScopeProvider>(),
            new AIRuntimeContextContributorCollection(() => []));
    }

    private IChatClient CreateClient(IChatClient inner)
        => new AIProfileSystemPromptChatMiddleware(_contextAccessorMock.Object).Apply(inner);
}
