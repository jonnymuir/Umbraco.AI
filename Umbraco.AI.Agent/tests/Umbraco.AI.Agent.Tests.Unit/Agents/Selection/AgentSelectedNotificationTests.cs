// S4 - I can react to picks without writing a selector (AC1-AC6; AC7 lives in the controller spec)
using Shouldly;
using Umbraco.AI.Agent.Core.Agents;
using Umbraco.AI.Agent.Core.Agents.Selection;
using Xunit;
using static Umbraco.AI.Agent.Tests.Unit.Agents.Selection.AgentSelectionTestBuilders;
using UmbracoAIAgent = Umbraco.AI.Agent.Core.Agents.AIAgent;

namespace Umbraco.AI.Agent.Tests.Unit.Agents.Selection;

public class AgentSelectedNotificationTests
{
    private static readonly UmbracoAIAgent AgentA = CreateAgent(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "agent-a");
    private static readonly UmbracoAIAgent AgentB = CreateAgent(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), "agent-b");

    // ---- Happy path --------------------------------------------------------------------------

    // AC1 - Published once per pick / AC2 - Carries the result / AC3 - Carries the request the selectors saw
    public class GivenASelectorPicksAnAgent
    {
        private readonly RecordingSelector _selector = RecordingSelector.Returning(AgentB, "my-rule", "editing products");
        private readonly IReadOnlyList<AIAgentSelectedNotification> _published;

        public GivenASelectorPicksAnAgent()
        {
            var builder = new SelectionServiceBuilder().WithAgents(AgentA, AgentB).WithSelectors(_selector);
            builder.Build().SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
            _published = builder.PublishedNotifications;
        }

        [Fact]
        public void PublishesExactlyOneNotification() => _published.Count.ShouldBe(1);

        [Fact]
        public void CarriesTheSelectionResult()
            => _published.Single().Selection.ShouldBe(new AIAgentSelectionResult(AgentB, "my-rule", "editing products"));

        [Fact]
        public void CarriesTheRequestTheSelectorsSaw()
            => _published.Single().Request.ShouldBeSameAs(_selector.Requests.Single());
    }

    // AC4 - Single candidate still notifies
    public class GivenExactlyOneCandidate
    {
        private readonly IReadOnlyList<AIAgentSelectedNotification> _published;

        public GivenExactlyOneCandidate()
        {
            var builder = new SelectionServiceBuilder().WithAgents(AgentA);
            builder.Build().SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
            _published = builder.PublishedNotifications;
        }

        [Fact]
        public void PublishesWithTheOnlyCandidateSelectorId()
            => _published.Single().Selection.SelectorId.ShouldBe("only-candidate");
    }

    // AC5 - Fallback still notifies
    public class GivenEverySelectorHasNoOpinion
    {
        private readonly IReadOnlyList<AIAgentSelectedNotification> _published;

        public GivenEverySelectorHasNoOpinion()
        {
            var builder = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB)
                .WithSelectors(RecordingSelector.Returning(null));
            builder.Build().SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
            _published = builder.PublishedNotifications;
        }

        [Fact]
        public void PublishesWithTheFallbackSelectorId()
            => _published.Single().Selection.SelectorId.ShouldBe("fallback");
    }

    // ---- Sad path / edge ---------------------------------------------------------------------

    // AC6 - No candidates, no notification
    public class GivenNoCandidates
    {
        private readonly IReadOnlyList<AIAgentSelectedNotification> _published;

        public GivenNoCandidates()
        {
            var builder = new SelectionServiceBuilder()
                .WithAgents(CreateAgent(Guid.NewGuid(), "inactive", isActive: false));
            builder.Build().SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
            _published = builder.PublishedNotifications;
        }

        [Fact]
        public void PublishesNoNotification() => _published.ShouldBeEmpty();
    }
}
