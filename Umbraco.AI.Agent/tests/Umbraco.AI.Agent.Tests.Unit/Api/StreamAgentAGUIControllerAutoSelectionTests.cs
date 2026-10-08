// S1/S3/S4/S5 - Auto agent selection through the real StreamAgentAGUI controller action
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Shouldly;
using Umbraco.AI.Agent.Core.Agents;
using Umbraco.AI.Agent.Core.Agents.Selection;
using Umbraco.AI.AGUI.Models;
using Umbraco.AI.AGUI.Streaming;
using Umbraco.AI.Web.Api.Common.Models;
using Xunit;
using static Umbraco.AI.Agent.Tests.Unit.Agents.Selection.AgentSelectionTestBuilders;
using static Umbraco.AI.Agent.Tests.Unit.Agents.Selection.AgentSelectionTestHarness;
using UmbracoAIAgent = Umbraco.AI.Agent.Core.Agents.AIAgent;

namespace Umbraco.AI.Agent.Tests.Unit.Api;

public class StreamAgentAGUIControllerAutoSelectionTests
{
    private static readonly UmbracoAIAgent AgentA = CreateAgent(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "agent-a");
    private static readonly UmbracoAIAgent AgentB = CreateAgent(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), "agent-b");

    private static readonly IdOrAlias Auto = new("auto");

    // ---- Happy path --------------------------------------------------------------------------

    // S3 AC1/AC2/AC3 - agent_selected carries selector ID, reason, and the existing fields
    public class GivenACustomSelectorPicksAgentB
    {
        private readonly ControllerHarness _harness = new(new AIAgentSelectionResult(AgentB, "my-rule", "editing products"));
        private readonly JsonElement _event;

        public GivenACustomSelectorPicksAgentB()
        {
            var result = _harness.Controller.StreamAgentAGUI(Auto, CreateRunRequest()).GetAwaiter().GetResult();
            _event = ReadFirstEventValueAsync(result).GetAwaiter().GetResult();
        }

        [Fact]
        public void CarriesTheSelectorId() => _event.GetProperty("selectorId").GetString().ShouldBe("my-rule");

        [Fact]
        public void CarriesTheReason() => _event.GetProperty("reason").GetString().ShouldBe("editing products");

        [Fact]
        public void KeepsTheAgentId() => _event.GetProperty("agentId").GetGuid().ShouldBe(AgentB.Id);

        [Fact]
        public void KeepsTheAgentName() => _event.GetProperty("agentName").GetString().ShouldBe(AgentB.Name);

        [Fact]
        public void KeepsTheAgentAlias() => _event.GetProperty("agentAlias").GetString().ShouldBe(AgentB.Alias);

        [Fact]
        public void StartsTheRunForAgentB() => _harness.StreamedAgentId.ShouldBe(AgentB.Id);
    }

    // S3 AC6 - Fallback is recorded
    public class GivenTheSelectionFellBack
    {
        private readonly JsonElement _event;

        public GivenTheSelectionFellBack()
        {
            var harness = new ControllerHarness(new AIAgentSelectionResult(AgentA, "fallback", null));
            var result = harness.Controller.StreamAgentAGUI(Auto, CreateRunRequest()).GetAwaiter().GetResult();
            _event = ReadFirstEventValueAsync(result).GetAwaiter().GetResult();
        }

        [Fact]
        public void TheEventSelectorIdIsFallback() => _event.GetProperty("selectorId").GetString().ShouldBe("fallback");
    }

    // S3 AC9 - Run options otherwise unchanged
    public class GivenAnAutoPick
    {
        private static readonly AIAgentSelectionResult Selection = new(AgentB, "my-rule", null);
        private readonly ControllerHarness _harness = new(Selection);

        public GivenAnAutoPick()
            => _harness.Controller.StreamAgentAGUI(Auto, CreateRunRequest()).GetAwaiter().GetResult();

        [Fact]
        public void PassesDefaultOptionsPlusTheSelection()
            => _harness.CapturedOptions.ShouldBeEquivalentTo(new AIAgentExecutionOptions { Selection = Selection });
    }

    // S5 AC1 - Previous agent is forwarded to selection
    public class GivenAValidPreviousAgentId
    {
        private readonly ControllerHarness _harness = new(new AIAgentSelectionResult(AgentA, "sticky", null));

        public GivenAValidPreviousAgentId()
            => _harness.Controller
                .StreamAgentAGUI(Auto, CreateRunRequest($$"""{ "previousAgentId": "{{AgentA.Id}}" }"""))
                .GetAwaiter().GetResult();

        [Fact]
        public void ForwardsThePreviousAgentId() => _harness.CapturedInput!.PreviousAgentId.ShouldBe(AgentA.Id);
    }

    // ---- Sad path / edge ---------------------------------------------------------------------

    // S1 AC18 - No candidates
    public class GivenNoCandidates
    {
        private readonly IResult _result;

        public GivenNoCandidates()
            => _result = new ControllerHarness(selection: null).Controller
                .StreamAgentAGUI(Auto, CreateRunRequest()).GetAwaiter().GetResult();

        [Fact]
        public void Returns404NoActiveAgentsFound()
            => _result.ShouldBeOfType<NotFound<ProblemDetails>>().Value!.Title.ShouldBe("No active agents found");
    }

    // S1 AC19 - Missing surface
    public class GivenNoSurface
    {
        private readonly IResult _result;

        public GivenNoSurface()
            => _result = new ControllerHarness(new AIAgentSelectionResult(AgentA, "x", null), surface: null).Controller
                .StreamAgentAGUI(Auto, CreateRunRequest()).GetAwaiter().GetResult();

        [Fact]
        public void Returns400SurfaceIsRequired()
            => _result.ShouldBeOfType<BadRequest<ProblemDetails>>().Value!.Title.ShouldBe("Surface is required for auto agent selection");
    }

    // S1 AC20 - Explicit agents are untouched / S4 AC7 - Explicit agents, no notification
    // (the notification is only ever published by IAIAgentSelectionService, so "never selected" means "never notified")
    public class GivenAnExplicitAgent
    {
        private readonly ControllerHarness _harness = new(selection: null, explicitAgent: AgentB);

        public GivenAnExplicitAgent()
            => _harness.Controller.StreamAgentAGUI(new IdOrAlias(AgentB.Id), CreateRunRequest()).GetAwaiter().GetResult();

        [Fact]
        public void CallsNoSelection()
            => _harness.SelectionService.Verify(
                x => x.SelectAgentAsync(It.IsAny<AIAgentSelectionInput>(), It.IsAny<CancellationToken>()),
                Times.Never);
    }

    // S5 AC7 - Garbage previous ID
    public class GivenAPreviousAgentIdThatIsNotAGuid
    {
        private readonly ControllerHarness _harness = new(new AIAgentSelectionResult(AgentA, "llm", null));
        private readonly IResult _result;

        public GivenAPreviousAgentIdThatIsNotAGuid()
            => _result = _harness.Controller
                .StreamAgentAGUI(Auto, CreateRunRequest("""{ "previousAgentId": "not-a-guid" }"""))
                .GetAwaiter().GetResult();

        [Fact]
        public void StillStreams() => _result.ShouldBeOfType<AGUIEventStreamResult>();

        [Fact]
        public void PassesNoPreviousAgentId() => _harness.CapturedInput!.PreviousAgentId.ShouldBeNull();
    }

    // S5 AC7 - previousAgentId is a JSON number, not a string
    public class GivenAPreviousAgentIdThatIsANumber
    {
        private readonly ControllerHarness _harness = new(new AIAgentSelectionResult(AgentA, "llm", null));

        public GivenAPreviousAgentIdThatIsANumber()
            => _harness.Controller
                .StreamAgentAGUI(Auto, CreateRunRequest("""{ "previousAgentId": 123 }"""))
                .GetAwaiter().GetResult();

        [Fact]
        public void PassesNoPreviousAgentId() => _harness.CapturedInput!.PreviousAgentId.ShouldBeNull();
    }

    // S5 AC7 - previousAgentId is JSON null
    public class GivenAPreviousAgentIdThatIsJsonNull
    {
        private readonly ControllerHarness _harness = new(new AIAgentSelectionResult(AgentA, "llm", null));

        public GivenAPreviousAgentIdThatIsJsonNull()
            => _harness.Controller
                .StreamAgentAGUI(Auto, CreateRunRequest("""{ "previousAgentId": null }"""))
                .GetAwaiter().GetResult();

        [Fact]
        public void PassesNoPreviousAgentId() => _harness.CapturedInput!.PreviousAgentId.ShouldBeNull();
    }

    // S5 AC7 - previousAgentId is a nested object, not a scalar
    public class GivenAPreviousAgentIdThatIsANestedObject
    {
        private readonly ControllerHarness _harness = new(new AIAgentSelectionResult(AgentA, "llm", null));

        public GivenAPreviousAgentIdThatIsANestedObject()
            => _harness.Controller
                .StreamAgentAGUI(Auto, CreateRunRequest("""{ "previousAgentId": { "foo": "bar" } }"""))
                .GetAwaiter().GetResult();

        [Fact]
        public void PassesNoPreviousAgentId() => _harness.CapturedInput!.PreviousAgentId.ShouldBeNull();
    }

    // S5 AC10 - Explicit agents ignore previousAgentId
    public class GivenAnExplicitAgentWithAPreviousAgentId
    {
        private readonly ControllerHarness _harness = new(selection: null, explicitAgent: AgentB);

        public GivenAnExplicitAgentWithAPreviousAgentId()
            => _harness.Controller
                .StreamAgentAGUI(new IdOrAlias(AgentB.Id), CreateRunRequest($$"""{ "previousAgentId": "{{AgentA.Id}}" }"""))
                .GetAwaiter().GetResult();

        [Fact]
        public void RunsTheExplicitAgent() => _harness.StreamedAgentId.ShouldBe(AgentB.Id);

        [Fact]
        public void CallsNoSelection()
            => _harness.SelectionService.Verify(
                x => x.SelectAgentAsync(It.IsAny<AIAgentSelectionInput>(), It.IsAny<CancellationToken>()),
                Times.Never);
    }
}
