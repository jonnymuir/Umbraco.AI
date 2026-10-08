// S2 - My rules can see the full request context (plus S5 AC1, AC6: previous agent resolution)
using Microsoft.Extensions.AI;
using Shouldly;
using Umbraco.AI.Agent.Core.Agents.Selection;
using Umbraco.AI.Core.RuntimeContext;
using Xunit;
using static Umbraco.AI.Agent.Tests.Unit.Agents.Selection.AgentSelectionTestBuilders;
using UmbracoAIAgent = Umbraco.AI.Agent.Core.Agents.AIAgent;

namespace Umbraco.AI.Agent.Tests.Unit.Agents.Selection;

public class AgentSelectionRequestTests
{
    private static readonly UmbracoAIAgent AgentA = CreateAgent(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "agent-a");
    private static readonly UmbracoAIAgent AgentD = CreateAgent(Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000d"), "agent-d");
    private static readonly UmbracoAIAgent OutOfScopeAgent =
        CreateAgent(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), "agent-b", allowedSection: "settings");
    private static readonly UmbracoAIAgent InactiveAgent =
        CreateAgent(Guid.Parse("cccccccc-0000-0000-0000-000000000003"), "agent-c", isActive: false);

    /// <summary>Two candidates, so the selector actually runs, and a recording selector captures the request.</summary>
    private static AIAgentSelectionRequest CaptureRequest(AIAgentSelectionInput input, params Guid[] userGroupIds)
    {
        var recorder = RecordingSelector.Returning(null);
        var service = new SelectionServiceBuilder()
            .WithAgents(AgentA, AgentD, OutOfScopeAgent, InactiveAgent)
            .WithSelectors(recorder)
            .WithUserGroups(userGroupIds)
            .Build();

        service.SelectAgentAsync(input).GetAwaiter().GetResult();
        return recorder.Requests.Single();
    }

    // ---- Happy path --------------------------------------------------------------------------

    // AC1 - Full conversation
    public class GivenAConversationWithAnImageAttachment
    {
        private static readonly DataContent Image = new(new byte[] { 1, 2, 3 }, "image/png");
        private readonly AIAgentSelectionRequest _request;

        public GivenAConversationWithAnImageAttachment()
            => _request = CaptureRequest(CreateInput(messages:
            [
                new ChatMessage(ChatRole.User, "first"),
                new ChatMessage(ChatRole.Assistant, "reply"),
                new ChatMessage(ChatRole.User, [new TextContent("look"), Image]),
            ]));

        [Fact]
        public void HoldsAllThreeMessages() => _request.Messages.Count.ShouldBe(3);

        [Fact]
        public void IncludesTheAttachmentContent() => _request.Messages[2].Contents.ShouldContain(Image);
    }

    // AC2 - All context items
    public class GivenAnEntityKeyContextItem
    {
        private static readonly AIRequestContextItem EntityKey = new() { Description = "entity key", Value = "1234" };
        private readonly AIAgentSelectionRequest _request;

        public GivenAnEntityKeyContextItem()
            => _request = CaptureRequest(CreateInput(contextItems: [EntityKey]));

        [Fact]
        public void ContainsThatContextItem() => _request.ContextItems.ShouldContain(EntityKey);
    }

    // AC3 - Availability context
    public class GivenARequestFromADocumentInTheContentSection
    {
        private readonly AIAgentSelectionRequest _request;

        public GivenARequestFromADocumentInTheContentSection()
            => _request = CaptureRequest(CreateInput(section: "content", entityType: "document"));

        [Fact]
        public void HasTheContentSection() => _request.AvailabilityContext.Section.ShouldBe("content");

        [Fact]
        public void HasTheDocumentEntityType() => _request.AvailabilityContext.EntityType.ShouldBe("document");
    }

    // AC4 - User groups
    public class GivenAUserInTwoGroups
    {
        private static readonly Guid G1 = Guid.Parse("11111111-0000-0000-0000-000000000001");
        private static readonly Guid G2 = Guid.Parse("22222222-0000-0000-0000-000000000002");
        private readonly AIAgentSelectionRequest _request;

        public GivenAUserInTwoGroups()
            => _request = CaptureRequest(CreateInput(), G1, G2);

        [Fact]
        public void HasBothGroupIds() => _request.UserGroupIds.ShouldBe([G1, G2]);
    }

    // AC5 - Frontend tools
    public class GivenTwoFrontendTools
    {
        private readonly AIAgentSelectionRequest _request;

        public GivenTwoFrontendTools()
            => _request = CaptureRequest(CreateInput(frontendTools: [CreateFrontendTool("one"), CreateFrontendTool("two")]));

        [Fact]
        public void HoldsBothTools() => _request.FrontendTools.Count.ShouldBe(2);
    }

    // AC6 - Candidates only. With one candidate no selector runs, so read the request the
    // service built from the AIAgentSelectedNotification it publishes (S4).
    public class GivenOneInScopeOneOutOfScopeAndOneInactiveAgent
    {
        private readonly AIAgentSelectionRequest _request;

        public GivenOneInScopeOneOutOfScopeAndOneInactiveAgent()
        {
            var builder = new SelectionServiceBuilder().WithAgents(AgentA, OutOfScopeAgent, InactiveAgent);
            builder.Build().SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
            _request = builder.PublishedNotifications.Single().Request;
        }

        [Fact]
        public void OnlyTheInScopeActiveAgentIsACandidate() => _request.CandidateAgents.ShouldBe([AgentA]);
    }

    // S5 AC1 - Previous agent is resolved
    public class GivenAPreviousAgentThatIsACandidate
    {
        private readonly AIAgentSelectionRequest _request;

        public GivenAPreviousAgentThatIsACandidate()
            => _request = CaptureRequest(CreateInput(previousAgentId: AgentA.Id));

        [Fact]
        public void ResolvesThePreviousAgent() => _request.PreviousAgent.ShouldBe(AgentA);
    }

    // ---- Sad path / edge ---------------------------------------------------------------------

    // S5 AC6 - Previous agent no longer allowed
    public class GivenAPreviousAgentThatIsNotACandidate
    {
        private readonly AIAgentSelectionRequest _request;

        public GivenAPreviousAgentThatIsNotACandidate()
            => _request = CaptureRequest(CreateInput(previousAgentId: OutOfScopeAgent.Id));

        [Fact]
        public void LeavesThePreviousAgentNull() => _request.PreviousAgent.ShouldBeNull();
    }
}
