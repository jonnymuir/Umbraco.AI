// S1 - Plug in my own agent selection rule (default LLM classifier: AC5, AC6, AC16, AC17)
using Microsoft.Extensions.AI;
using Shouldly;
using Umbraco.AI.Agent.Core.Agents.Selection;
using Xunit;
using static Umbraco.AI.Agent.Tests.Unit.Agents.Selection.AgentSelectionTestBuilders;
using UmbracoAIAgent = Umbraco.AI.Agent.Core.Agents.AIAgent;

namespace Umbraco.AI.Agent.Tests.Unit.Agents.Selection;

public class LLMAgentSelectorTests
{
    private const string AgentAId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string AgentBId = "bbbbbbbb-0000-0000-0000-000000000002";

    private static readonly UmbracoAIAgent AgentA =
        CreateAgent(Guid.Parse(AgentAId), "agent-a", description: "Handles product copy");
    private static readonly UmbracoAIAgent AgentB =
        CreateAgent(Guid.Parse(AgentBId), "agent-b", description: "Handles SEO metadata");

    /// <summary>Runs the real selection service with only the real LLMAgentSelector registered.</summary>
    private static AIAgentSelectionResult? SelectWithOnlyTheLLMSelector(string? classifierReply)
    {
        var (selector, _) = CreateLLMSelector(classifierReply);
        var service = new SelectionServiceBuilder()
            .WithAgents(AgentA, AgentB)
            .WithSelectors(selector)
            .Build();

        return service.SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
    }

    // ---- Happy path --------------------------------------------------------------------------

    // AC5 - Default behaviour is unchanged
    public class GivenTheClassifierRepliesWithCandidateB
    {
        private readonly AIAgentSelectionResult? _result;

        public GivenTheClassifierRepliesWithCandidateB()
            => _result = SelectWithOnlyTheLLMSelector(AgentBId);

        [Fact]
        public void SelectsAgentB() => _result!.Agent.ShouldBe(AgentB);
    }

    // AC6 - LLM selector keeps today's input
    public class GivenASeveralMessageConversation
    {
        private readonly string _prompt;

        public GivenASeveralMessageConversation()
        {
            var (selector, sentPrompts) = CreateLLMSelector(AgentAId);
            var request = CreateRequest(
                [AgentA, AgentB],
                messages:
                [
                    new ChatMessage(ChatRole.User, "earlier user message"),
                    new ChatMessage(ChatRole.Assistant, "assistant reply"),
                    new ChatMessage(ChatRole.User, "latest user message"),
                ]);

            selector.SelectAgentAsync(request).GetAwaiter().GetResult();
            _prompt = sentPrompts.Single().Single().Text;
        }

        [Fact]
        public void ContainsTheLastUserMessage() => _prompt.ShouldContain("latest user message");

        [Fact]
        public void OmitsEarlierUserMessages() => _prompt.ShouldNotContain("earlier user message");

        [Fact]
        public void OmitsAssistantMessages() => _prompt.ShouldNotContain("assistant reply");

        [Theory]
        [InlineData(AgentAId)]
        [InlineData("agent-a name")]
        [InlineData("Handles product copy")]
        [InlineData(AgentBId)]
        [InlineData("agent-b name")]
        [InlineData("Handles SEO metadata")]
        public void ListsEveryCandidatesIdNameAndDescription(string expected) => _prompt.ShouldContain(expected);
    }

    // ---- Sad path / edge ---------------------------------------------------------------------

    // AC16 - LLM failure falls through
    public class GivenNoClassifierOrDefaultChatProfile
    {
        private readonly AIAgentSelectionResult? _result;

        public GivenNoClassifierOrDefaultChatProfile()
            => _result = SelectWithOnlyTheLLMSelector(classifierReply: null);

        [Fact]
        public void SelectsTheFirstCandidate() => _result!.Agent.ShouldBe(AgentA);

        [Fact]
        public void RecordsTheFallbackSelectorId() => _result!.SelectorId.ShouldBe("fallback");
    }

    // AC17 - LLM unparseable reply falls through
    public class GivenAClassifierReplyWithNoGuid
    {
        private readonly AIAgentSelectionResult? _result;

        public GivenAClassifierReplyWithNoGuid()
            => _result = SelectWithOnlyTheLLMSelector("I think the SEO one");

        [Fact]
        public void SelectsTheFirstCandidate() => _result!.Agent.ShouldBe(AgentA);

        [Fact]
        public void RecordsTheFallbackSelectorId() => _result!.SelectorId.ShouldBe("fallback");
    }
}
