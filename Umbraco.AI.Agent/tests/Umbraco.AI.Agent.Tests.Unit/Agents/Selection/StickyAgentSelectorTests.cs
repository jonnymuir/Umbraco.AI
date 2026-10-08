// S5 - Rules can keep the same agent across a conversation (AC2, AC8)
// AC9 ("sticky is off by default") needs the real AddUmbracoAIAgentCore composer registrations, which
// can't run outside a CMS host (Umbraco's TypeLoader), so it is verified on the demo site in T11.
using Shouldly;
using Umbraco.AI.Agent.Core.Agents.Selection;
using Xunit;
using static Umbraco.AI.Agent.Tests.Unit.Agents.Selection.AgentSelectionTestBuilders;
using UmbracoAIAgent = Umbraco.AI.Agent.Core.Agents.AIAgent;

namespace Umbraco.AI.Agent.Tests.Unit.Agents.Selection;

public class StickyAgentSelectorTests
{
    private static readonly UmbracoAIAgent AgentA = CreateAgent(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "agent-a");
    private static readonly UmbracoAIAgent AgentB = CreateAgent(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), "agent-b");

    // ---- Happy path --------------------------------------------------------------------------

    // AC2 - Sticky keeps the previous agent
    public class GivenStickyBeforeASelectorThatWouldSwitch
    {
        private readonly AIAgentSelectionResult? _result;

        public GivenStickyBeforeASelectorThatWouldSwitch()
        {
            var service = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB)
                .WithSelectors(new StickyAgentSelector(), RecordingSelector.Returning(AgentB, "would-switch"))
                .Build();

            _result = service.SelectAgentAsync(CreateInput(previousAgentId: AgentA.Id)).GetAwaiter().GetResult();
        }

        [Fact]
        public void KeepsThePreviousAgent() => _result!.Agent.ShouldBe(AgentA);

        [Fact]
        public void RecordsTheStickySelectorId() => _result!.SelectorId.ShouldBe("sticky");
    }

    // ---- Sad path / edge ---------------------------------------------------------------------

    // AC8 - Sticky with no previous pick
    public class GivenNoPreviousPick
    {
        private readonly AIAgentSelectionResult? _stickyResult;
        private readonly AIAgentSelectionResult? _serviceResult;

        public GivenNoPreviousPick()
        {
            _stickyResult = new StickyAgentSelector()
                .SelectAgentAsync(CreateRequest([AgentA, AgentB], previousAgent: null))
                .GetAwaiter().GetResult();

            var service = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB)
                .WithSelectors(new StickyAgentSelector(), RecordingSelector.Returning(AgentB, "next"))
                .Build();

            _serviceResult = service.SelectAgentAsync(CreateInput(previousAgentId: null)).GetAwaiter().GetResult();
        }

        [Fact]
        public void StickyHasNoOpinion() => _stickyResult.ShouldBeNull();

        [Fact]
        public void TheNextSelectorDecides() => _serviceResult!.Agent.ShouldBe(AgentB);
    }
}
