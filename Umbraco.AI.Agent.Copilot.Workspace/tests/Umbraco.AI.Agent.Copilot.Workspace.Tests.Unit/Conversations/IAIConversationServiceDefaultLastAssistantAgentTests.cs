// IAIConversationService.GetLastAssistantAgentIdAsync default body (keeps third-party implementations compiling)
using Moq;
using Shouldly;
using Umbraco.AI.Agent.Conversations.Core.Conversations;
using Xunit;

namespace Umbraco.AI.Agent.Copilot.Workspace.Tests.Unit.Conversations;

/// <summary>
/// The default interface body of <see cref="IAIConversationService.GetLastAssistantAgentIdAsync"/>,
/// used by implementations written before the method existed. It must find the same message the
/// real repository query does (the newest assistant message) using only
/// <see cref="IAIConversationService.GetMessagesPagedAsync"/>.
/// </summary>
public class IAIConversationServiceDefaultLastAssistantAgentTests
{
    private static readonly Guid AgentA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AgentB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    public class GivenTheNewestAssistantMessageIsOnTheLastPage
    {
        [Fact]
        public async Task ReturnsItsAgent()
        {
            var service = ServiceWithMessages(
                Message("user"), Message("assistant", AgentA), Message("user"), Message("assistant", AgentB), Message("user"));

            (await service.GetLastAssistantAgentIdAsync(Guid.Empty)).ShouldBe(AgentB);
        }
    }

    public class GivenTheNewestAssistantMessageIsSeveralPagesBack
    {
        [Fact]
        public async Task ReturnsItsAgent()
        {
            var messages = new List<AIMessage> { Message("assistant", AgentA) };
            messages.AddRange(Enumerable.Range(0, 120).Select(_ => Message("user")));

            (await ServiceWithMessages([.. messages]).GetLastAssistantAgentIdAsync(Guid.Empty)).ShouldBe(AgentA);
        }
    }

    public class GivenTheNewestAssistantMessageHasNoAgent
    {
        [Fact]
        public async Task ReturnsNull_NotAnOlderMessagesAgent()
        {
            var service = ServiceWithMessages(Message("assistant", AgentA), Message("assistant", agentId: null));

            (await service.GetLastAssistantAgentIdAsync(Guid.Empty)).ShouldBeNull();
        }
    }

    public class GivenNoAssistantMessages
    {
        [Fact]
        public async Task ReturnsNull()
            => (await ServiceWithMessages(Message("user")).GetLastAssistantAgentIdAsync(Guid.Empty)).ShouldBeNull();
    }

    public class GivenAnEmptyConversation
    {
        [Fact]
        public async Task ReturnsNull()
            => (await ServiceWithMessages().GetLastAssistantAgentIdAsync(Guid.Empty)).ShouldBeNull();
    }

    public class GivenAConversationTheUserDoesNotOwn
    {
        [Fact]
        public async Task PropagatesTheOwnershipFailure()
        {
            var service = new Mock<IAIConversationService> { CallBase = true };
            service
                .Setup(s => s.GetMessagesPagedAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("not owned"));

            await Should.ThrowAsync<InvalidOperationException>(() => service.Object.GetLastAssistantAgentIdAsync(Guid.Empty));
        }
    }

    private static AIMessage Message(string role, Guid? agentId = null)
        => new() { Id = Guid.NewGuid(), Role = role, AgentId = agentId };

    /// <summary>
    /// An implementation that only provides <see cref="IAIConversationService.GetMessagesPagedAsync"/>
    /// (as a pre-existing third-party one would) and inherits the default body.
    /// </summary>
    private static IAIConversationService ServiceWithMessages(params AIMessage[] messages)
    {
        var service = new Mock<IAIConversationService> { CallBase = true };
        service
            .Setup(s => s.GetMessagesPagedAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, int skip, int take, CancellationToken _) =>
                ((IReadOnlyList<AIMessage>)messages.Skip(skip).Take(take).ToList(), messages.Length));
        return service.Object;
    }
}
