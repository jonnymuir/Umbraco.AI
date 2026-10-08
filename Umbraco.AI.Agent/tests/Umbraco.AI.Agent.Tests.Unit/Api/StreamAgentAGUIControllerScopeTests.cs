using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Shouldly;
using Umbraco.AI.Agent.Core.AGUI;
using Umbraco.AI.Agent.Core.Agents;
using Umbraco.AI.Agent.Core.Agents.Selection;
using Umbraco.AI.Agent.Core.Surfaces;
using Umbraco.AI.Agent.Web.Api.Management.Agent.Controllers;
using Umbraco.AI.AGUI.Models;
using Umbraco.AI.Core.RuntimeContext;
using Umbraco.AI.Web.Api.Common.Models;
using Xunit;
using AgentConstants = Umbraco.AI.Agent.Core.Constants;
using CoreConstants = Umbraco.AI.Core.Constants;
using UmbracoAIAgent = Umbraco.AI.Agent.Core.Agents.AIAgent;

namespace Umbraco.AI.Agent.Tests.Unit.Api;

/// <summary>
/// The auto-selection path filters agents through <see cref="AIAgentScopeValidator"/>. These tests
/// pin that an explicitly named agent goes through the same check - active, opted in to the surface
/// via <c>SurfaceIds</c>, and inside its scope - so passing an agent ID is not a way around rules the
/// surface has applied.
/// </summary>
public class StreamAgentAGUIControllerScopeTests
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class TestSurface : IAIAgentSurface
    {
        public string Id => "copilot";
        public string Icon => "icon-robot";
        public IReadOnlyList<string> SupportedScopeDimensions => ["section", "entityType"];
    }

    [Fact]
    public async Task StreamAgentAGUI_ExplicitAgentOutsideItsScope_IsRejected()
    {
        // Arrange
        // Agent is allowed only in the "settings" section; the request comes from "content".
        var agent = CreateAgent(allowedSection: "settings");
        var controller = CreateController(agent, requestSection: "content");

        // Act
        var result = await controller.StreamAgentAGUI(new IdOrAlias(AgentId), CreateRequest());

        // Assert
        var notFound = result.ShouldBeOfType<NotFound<ProblemDetails>>();
        notFound.Value!.Title.ShouldBe("AIAgent not available in this context");
    }

    [Fact]
    public async Task StreamAgentAGUI_ExplicitAgentInsideItsScope_IsAllowed()
    {
        // Arrange
        var agent = CreateAgent(allowedSection: "content");
        var controller = CreateController(agent, requestSection: "content");

        // Act
        var result = await controller.StreamAgentAGUI(new IdOrAlias(AgentId), CreateRequest());

        // Assert
        result.ShouldNotBeOfType<NotFound<ProblemDetails>>();
    }

    [Fact]
    public async Task StreamAgentAGUI_WithNoSurfaceInContext_SkipsScopeCheck()
    {
        // Arrange
        // Programmatic callers send no surface. Scope rules are surface-relative, so there is
        // nothing to validate against and these callers must keep working.
        var agent = CreateAgent(allowedSection: "settings");
        var controller = CreateController(agent, requestSection: "content", surface: null);

        // Act
        var result = await controller.StreamAgentAGUI(new IdOrAlias(AgentId), CreateRequest());

        // Assert
        result.ShouldNotBeOfType<NotFound<ProblemDetails>>();
    }

    [Fact]
    public async Task StreamAgentAGUI_AgentWithNoScopeRules_IsAllowed()
    {
        // Arrange
        var agent = CreateAgent(allowedSection: null);
        var controller = CreateController(agent, requestSection: "content");

        // Act
        var result = await controller.StreamAgentAGUI(new IdOrAlias(AgentId), CreateRequest());

        // Assert
        result.ShouldNotBeOfType<NotFound<ProblemDetails>>();
    }

    [Fact]
    public async Task StreamAgentAGUI_ExplicitAgentNotOptedInToTheSurface_IsRejected()
    {
        // Arrange
        // No scope rules at all, so only the SurfaceIds opt-in can turn this agent away.
        var agent = CreateAgent(allowedSection: null, surfaceIds: ["some-other-surface"]);
        var controller = CreateController(agent, requestSection: "content");

        // Act
        var result = await controller.StreamAgentAGUI(new IdOrAlias(AgentId), CreateRequest());

        // Assert
        var notFound = result.ShouldBeOfType<NotFound<ProblemDetails>>();
        notFound.Value!.Title.ShouldBe("AIAgent not available in this context");
    }

    [Fact]
    public async Task StreamAgentAGUI_ExplicitAgentOnNoSurface_IsRejected()
    {
        // Arrange
        // An empty SurfaceIds list means "on no surface", not "on every surface".
        var agent = CreateAgent(allowedSection: null, surfaceIds: []);
        var controller = CreateController(agent, requestSection: "content");

        // Act
        var result = await controller.StreamAgentAGUI(new IdOrAlias(AgentId), CreateRequest());

        // Assert
        result.ShouldBeOfType<NotFound<ProblemDetails>>();
    }

    [Fact]
    public async Task StreamAgentAGUI_ExplicitAgentNotOptedIn_WithNoSurfaceInContext_StillRuns()
    {
        // Arrange
        // Opt-in is surface-relative like scope: a contextless programmatic caller keeps working.
        var agent = CreateAgent(allowedSection: null, surfaceIds: []);
        var controller = CreateController(agent, requestSection: "content", surface: null);

        // Act
        var result = await controller.StreamAgentAGUI(new IdOrAlias(AgentId), CreateRequest());

        // Assert
        result.ShouldNotBeOfType<NotFound<ProblemDetails>>();
    }

    [Fact]
    public async Task StreamAgentAGUI_ExplicitAgentOptedInWithDifferentCasing_IsAllowed()
    {
        // Arrange
        var agent = CreateAgent(allowedSection: null, surfaceIds: ["COPILOT"]);
        var controller = CreateController(agent, requestSection: "content");

        // Act
        var result = await controller.StreamAgentAGUI(new IdOrAlias(AgentId), CreateRequest());

        // Assert
        result.ShouldNotBeOfType<NotFound<ProblemDetails>>();
    }

    private static AGUIRunRequest CreateRequest()
        => new()
        {
            Messages = [],
            Context = [new AGUIContextItem { Description = "ctx", Value = "{}" }],
        };

    private static UmbracoAIAgent CreateAgent(string? allowedSection, IReadOnlyList<string>? surfaceIds = null)
        => new()
        {
            SurfaceIds = surfaceIds ?? ["copilot"],
            Id = AgentId,
            Alias = "test-agent",
            Name = "Test Agent",
            AgentType = AIAgentType.Standard,
            IsActive = true,
            Scope = allowedSection is null
                ? null
                : new AIAgentScope
                {
                    AllowRules = [new AIAgentScopeRule { Sections = [allowedSection] }],
                },
        };

    private static StreamAgentAGUIController CreateController(
        UmbracoAIAgent agent,
        string requestSection,
        string? surface = "copilot")
    {
        // TryGetAgentIdAsync is an extension method; for an IdOrAlias holding a GUID it returns the
        // ID without touching the service, so there is nothing to stub for it here.
        var agentServiceMock = new Mock<IAIAgentService>();
        agentServiceMock
            .Setup(x => x.GetAgentAsync(agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        agentServiceMock
            .Setup(x => x.StreamAgentAGUIAsync(
                It.IsAny<Guid>(),
                It.IsAny<AGUIRunRequest>(),
                It.IsAny<IEnumerable<AIFrontendTool>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(EmptyEventStream());

        var selectionServiceMock = new Mock<IAIAgentSelectionService>();

        var messageConverterMock = new Mock<IAGUIMessageConverter>();
        messageConverterMock
            .Setup(x => x.ConvertToChatMessages(It.IsAny<IEnumerable<AGUIMessage>?>()))
            .Returns([]);

        var contextConverterMock = new Mock<IAGUIContextConverter>();
        contextConverterMock
            .Setup(x => x.ConvertToRequestContextItems(It.IsAny<IEnumerable<AGUIContextItem>>()))
            .Returns([]);

        var toolConverterMock = new Mock<IAGUIToolConverter>();
        toolConverterMock
            .Setup(x => x.ConvertToFrontendTools(It.IsAny<IEnumerable<AGUITool>?>()))
            .Returns([]);

        // A real runtime context carrying the surface/section the request is supposed to have come
        // from — this is what BuildAvailabilityContext reads.
        var runtimeContext = new AIRuntimeContext([]);
        if (surface is not null)
        {
            runtimeContext.SetValue(AgentConstants.ContextKeys.Surface, surface);
        }

        runtimeContext.SetValue(CoreConstants.ContextKeys.Section, requestSection);

        var scopeMock = new Mock<IAIRuntimeContextScope>();
        scopeMock.Setup(x => x.Context).Returns(runtimeContext);

        var scopeProviderMock = new Mock<IAIRuntimeContextScopeProvider>();
        scopeProviderMock
            .Setup(x => x.CreateScope(It.IsAny<IEnumerable<AIRequestContextItem>>()))
            .Returns(scopeMock.Object);

        return new StreamAgentAGUIController(
            agentServiceMock.Object,
            selectionServiceMock.Object,
            messageConverterMock.Object,
            contextConverterMock.Object,
            toolConverterMock.Object,
            scopeProviderMock.Object,
            new AIRuntimeContextContributorCollection(() => []),
            new AIAgentScopeValidator(),
            new AIAgentSurfaceCollection(() => [new TestSurface()]));
    }

    private static async IAsyncEnumerable<Umbraco.AI.AGUI.Events.IAGUIEvent> EmptyEventStream()
    {
        await Task.CompletedTask;
        yield break;
    }
}
