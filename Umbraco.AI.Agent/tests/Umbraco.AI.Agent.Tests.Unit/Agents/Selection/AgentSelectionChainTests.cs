// S1 - Plug in my own agent selection rule (service-level criteria)
using Shouldly;
using Umbraco.AI.Agent.Core.Agents.Selection;
using Xunit;
using static Umbraco.AI.Agent.Tests.Unit.Agents.Selection.AgentSelectionTestBuilders;
using UmbracoAIAgent = Umbraco.AI.Agent.Core.Agents.AIAgent;

namespace Umbraco.AI.Agent.Tests.Unit.Agents.Selection;

public class AgentSelectionChainTests
{
    private static readonly UmbracoAIAgent AgentA = CreateAgent(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "agent-a");
    private static readonly UmbracoAIAgent AgentB = CreateAgent(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), "agent-b");
    private static readonly UmbracoAIAgent AgentC = CreateAgent(Guid.Parse("cccccccc-0000-0000-0000-000000000003"), "agent-c");

    // Not a candidate: only allowed in "settings", and every request here comes from "content".
    private static readonly UmbracoAIAgent OutOfScopeAgent =
        CreateAgent(Guid.Parse("dddddddd-0000-0000-0000-000000000004"), "agent-out-of-scope", allowedSection: "settings");

    // ---- Happy path --------------------------------------------------------------------------

    // AC1 - Custom selector decides
    public class GivenACustomSelectorThatReturnsACandidate
    {
        private readonly AIAgentSelectionResult? _result;

        public GivenACustomSelectorThatReturnsACandidate()
        {
            var service = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB, AgentC)
                .WithSelectors(RecordingSelector.Returning(AgentB, "custom"))
                .Build();

            _result = service.SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
        }

        [Fact]
        public void SelectsThatAgent() => _result!.Agent.ShouldBe(AgentB);
    }

    // AC2 - Collection order is respected / AC3 - Later selectors don't run once one decides
    public class GivenTwoSelectorsThatBothDecide
    {
        private readonly RecordingSelector _second = RecordingSelector.Returning(AgentC, "second");
        private readonly AIAgentSelectionResult? _result;

        public GivenTwoSelectorsThatBothDecide()
        {
            var service = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB, AgentC)
                .WithSelectors(RecordingSelector.Returning(AgentB, "first"), _second)
                .Build();

            _result = service.SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
        }

        [Fact]
        public void TheFirstSelectorInCollectionOrderWins() => _result!.Agent.ShouldBe(AgentB);

        [Fact]
        public void TheLaterSelectorIsNeverCalled() => _second.Requests.ShouldBeEmpty();
    }

    // AC4 - "No opinion" passes to the next selector
    public class GivenAFirstSelectorWithNoOpinion
    {
        private readonly AIAgentSelectionResult? _result;

        public GivenAFirstSelectorWithNoOpinion()
        {
            var service = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB, AgentC)
                .WithSelectors(RecordingSelector.Returning(null), RecordingSelector.Returning(AgentC, "next"))
                .Build();

            _result = service.SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
        }

        [Fact]
        public void TheNextSelectorDecides() => _result!.Agent.ShouldBe(AgentC);
    }

    // AC7 - Single candidate short-circuits / AC8 - No selector runs for a single candidate
    public class GivenExactlyOneCandidate
    {
        private readonly RecordingSelector _selector = RecordingSelector.Returning(AgentA, "custom");
        private readonly AIAgentSelectionResult? _result;

        public GivenExactlyOneCandidate()
        {
            var service = new SelectionServiceBuilder()
                .WithAgents(AgentA, OutOfScopeAgent)
                .WithSelectors(_selector)
                .Build();

            _result = service.SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
        }

        [Fact]
        public void SelectsTheOnlyCandidate() => _result!.Agent.ShouldBe(AgentA);

        [Fact]
        public void RecordsTheOnlyCandidateSelectorId() => _result!.SelectorId.ShouldBe("only-candidate");

        [Fact]
        public void CallsNoSelector() => _selector.Requests.ShouldBeEmpty();
    }

    // ---- Sad path / edge ---------------------------------------------------------------------

    // AC11 - Selector returns a non-candidate
    public class GivenASelectorThatReturnsANonCandidate
    {
        private readonly AIAgentSelectionResult? _result;

        public GivenASelectorThatReturnsANonCandidate()
        {
            var service = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB, AgentC, OutOfScopeAgent)
                .WithSelectors(RecordingSelector.Returning(OutOfScopeAgent, "rogue"), RecordingSelector.Returning(AgentC, "next"))
                .Build();

            _result = service.SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
        }

        [Fact]
        public void IgnoresItAndTheNextSelectorDecides() => _result!.Agent.ShouldBe(AgentC);
    }

    // Guarantee 2 - the service, not the selector, owns which object represents the candidate
    public class GivenASelectorThatReturnsADifferentInstanceWithACandidatesId
    {
        // Same Id as AgentB, but a different object with different state - a selector should never
        // be able to smuggle its own copy of an agent past the service.
        private static readonly UmbracoAIAgent StaleAgentB = CreateAgent(AgentB.Id, "agent-b-stale", isActive: false);

        private readonly AIAgentSelectionResult? _result;

        public GivenASelectorThatReturnsADifferentInstanceWithACandidatesId()
        {
            var service = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB, AgentC)
                .WithSelectors(RecordingSelector.Returning(StaleAgentB, "custom"))
                .Build();

            _result = service.SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
        }

        [Fact]
        public void ReturnsTheCandidateInstanceNotTheSelectorsCopy() => _result!.Agent.ShouldBeSameAs(AgentB);
    }

    // AC12 - Scope is never bypassed
    public class GivenEverySelectorReturnsANonCandidate
    {
        private readonly AIAgentSelectionResult? _result;

        public GivenEverySelectorReturnsANonCandidate()
        {
            var service = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB, OutOfScopeAgent)
                .WithSelectors(
                    RecordingSelector.Returning(OutOfScopeAgent, "rogue-1"),
                    RecordingSelector.Returning(OutOfScopeAgent, "rogue-2"))
                .Build();

            _result = service.SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
        }

        [Fact]
        public void TheSelectedAgentIsACandidate() => _result!.Agent.Id.ShouldBeOneOf(AgentA.Id, AgentB.Id);
    }

    // AC13 - Throwing selector is skipped
    public class GivenASelectorThatThrows
    {
        private readonly AIAgentSelectionResult? _result;

        public GivenASelectorThatThrows()
        {
            var service = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB, AgentC)
                .WithSelectors(
                    RecordingSelector.Throwing(new InvalidOperationException("broken rule")),
                    RecordingSelector.Returning(AgentC, "next"))
                .Build();

            _result = service.SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
        }

        [Fact]
        public void SkipsItAndTheNextSelectorDecides() => _result!.Agent.ShouldBe(AgentC);
    }

    // AC14 - Cancellation is not swallowed. Arrange actually cancels the token passed into
    // SelectAgentAsync - an OperationCanceledException whose token was never cancelled (e.g. an
    // HttpClient timeout) is a selector failure to skip, not cancellation to propagate, so this spec
    // has to prove the real-cancellation case still propagates on its own merits.
    public class GivenASelectorThatIsCancelled
    {
        private readonly IAIAgentSelectionService _service;
        private readonly CancellationTokenSource _cts = new();

        public GivenASelectorThatIsCancelled()
        {
            _service = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB)
                .WithSelectors(
                    RecordingSelector.Throwing(new OperationCanceledException()),
                    RecordingSelector.Returning(AgentB, "next"))
                .Build();
            _cts.Cancel();
        }

        [Fact]
        public async Task PropagatesTheCancellation()
            => await Should.ThrowAsync<OperationCanceledException>(() => _service.SelectAgentAsync(CreateInput(), _cts.Token));
    }

    // New - A selector timeout (TaskCanceledException from e.g. an HttpClient request timeout) does not
    // mean the caller's request was cancelled, so it's skipped like any other selector failure.
    public class GivenASelectorThatTimesOutWithoutTheRequestBeingCancelled
    {
        private readonly AIAgentSelectionResult? _result;

        public GivenASelectorThatTimesOutWithoutTheRequestBeingCancelled()
        {
            var service = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB, AgentC)
                .WithSelectors(
                    RecordingSelector.Throwing(new TaskCanceledException("The request timed out.")),
                    RecordingSelector.Returning(AgentC, "next"))
                .Build();

            // CreateInput()'s default CancellationToken is never cancelled, matching a selector whose
            // own HttpClient timeout fires independently of our request's token.
            _result = service.SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
        }

        [Fact]
        public void SkipsItAndTheNextSelectorDecides() => _result!.Agent.ShouldBe(AgentC);
    }

    // AC15 - Nobody decides
    public class GivenEverySelectorHasNoOpinion
    {
        private readonly AIAgentSelectionResult? _result;

        public GivenEverySelectorHasNoOpinion()
        {
            var service = new SelectionServiceBuilder()
                .WithAgents(AgentA, AgentB, AgentC)
                .WithSelectors(RecordingSelector.Returning(null), RecordingSelector.Returning(null))
                .Build();

            _result = service.SelectAgentAsync(CreateInput()).GetAwaiter().GetResult();
        }

        [Fact]
        public void SelectsTheFirstCandidate() => _result!.Agent.ShouldBe(AgentA);

        [Fact]
        public void RecordsTheFallbackSelectorId() => _result!.SelectorId.ShouldBe("fallback");
    }
}
