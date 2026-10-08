// Harness for the StreamAgentAGUIController wiring specs (PLAN.md T10).
// Everything that compiles without the controller wiring (the selection types, AIAgentSelectionService,
// LLMAgentSelector, StickyAgentSelector, and the obsolete SelectAgentForPromptAsync proxy) has its
// builders in AgentSelectionTestBuilders.cs instead - this file uses a static import of that one so
// there is only ever one CreateAgent/EmptyEventStream/etc.
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Moq;
using Umbraco.AI.Agent.Core.AGUI;
using Umbraco.AI.Agent.Core.Agents;
using Umbraco.AI.Agent.Core.Agents.Selection;
using Umbraco.AI.Agent.Core.Surfaces;
using Umbraco.AI.Agent.Web.Api.Management.Agent.Controllers;
using Umbraco.AI.AGUI.Models;
using Umbraco.AI.Core.RuntimeContext;
using static Umbraco.AI.Agent.Tests.Unit.Agents.Selection.AgentSelectionTestBuilders;
using AgentConstants = Umbraco.AI.Agent.Core.Constants;
using CoreConstants = Umbraco.AI.Core.Constants;
using UmbracoAIAgent = Umbraco.AI.Agent.Core.Agents.AIAgent;

namespace Umbraco.AI.Agent.Tests.Unit.Agents.Selection;

/// <summary>
/// Builders for the pieces of the agent-selection specs that exercise the controller wiring (T10).
/// </summary>
internal static class AgentSelectionTestHarness
{
    /// <summary>
    /// A real <see cref="StreamAgentAGUIController"/>. The selection service and agent service are mocked;
    /// scope validation, surfaces and context extraction are real, as in StreamAgentAGUIControllerScopeTests.
    /// </summary>
    public sealed class ControllerHarness
    {
        public ControllerHarness(
            AIAgentSelectionResult? selection,
            UmbracoAIAgent? explicitAgent = null,
            string? surface = SurfaceId,
            string section = "content")
        {
            SelectionService
                .Setup(x => x.SelectAgentAsync(It.IsAny<AIAgentSelectionInput>(), It.IsAny<CancellationToken>()))
                .Callback<AIAgentSelectionInput, CancellationToken>((input, _) => CapturedInput = input)
                .ReturnsAsync(selection);

            if (explicitAgent is not null)
            {
                AgentService
                    .Setup(x => x.GetAgentAsync(explicitAgent.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(explicitAgent);
            }

            AgentService
                .Setup(x => x.StreamAgentAGUIAsync(
                    It.IsAny<Guid>(), It.IsAny<AGUIRunRequest>(), It.IsAny<IEnumerable<AIFrontendTool>?>(), It.IsAny<CancellationToken>()))
                .Callback<Guid, AGUIRunRequest, IEnumerable<AIFrontendTool>?, CancellationToken>((id, _, _, _) => StreamedAgentId = id)
                .Returns(EmptyEventStream());

            AgentService
                .Setup(x => x.StreamAgentAGUIAsync(
                    It.IsAny<Guid>(), It.IsAny<AGUIRunRequest>(), It.IsAny<IEnumerable<AIFrontendTool>?>(),
                    It.IsAny<AIAgentExecutionOptions>(), It.IsAny<CancellationToken>()))
                .Callback<Guid, AGUIRunRequest, IEnumerable<AIFrontendTool>?, AIAgentExecutionOptions, CancellationToken>(
                    (id, _, _, options, _) =>
                    {
                        StreamedAgentId = id;
                        CapturedOptions = options;
                    })
                .Returns(EmptyEventStream());

            var messageConverter = new Mock<IAGUIMessageConverter>();
            messageConverter
                .Setup(x => x.ConvertToChatMessages(It.IsAny<IEnumerable<AGUIMessage>?>()))
                .Returns([]);

            var contextConverter = new Mock<IAGUIContextConverter>();
            contextConverter
                .Setup(x => x.ConvertToRequestContextItems(It.IsAny<IEnumerable<AGUIContextItem>>()))
                .Returns([]);

            var toolConverter = new Mock<IAGUIToolConverter>();
            toolConverter
                .Setup(x => x.ConvertToFrontendTools(It.IsAny<IEnumerable<AGUITool>?>()))
                .Returns([]);

            var runtimeContext = new AIRuntimeContext([]);
            if (surface is not null)
            {
                runtimeContext.SetValue(AgentConstants.ContextKeys.Surface, surface);
            }

            runtimeContext.SetValue(CoreConstants.ContextKeys.Section, section);

            var scope = new Mock<IAIRuntimeContextScope>();
            scope.Setup(x => x.Context).Returns(runtimeContext);

            var scopeProvider = new Mock<IAIRuntimeContextScopeProvider>();
            scopeProvider
                .Setup(x => x.CreateScope(It.IsAny<IEnumerable<AIRequestContextItem>>()))
                .Returns(scope.Object);

            Controller = new StreamAgentAGUIController(
                AgentService.Object,
                SelectionService.Object,
                messageConverter.Object,
                contextConverter.Object,
                toolConverter.Object,
                scopeProvider.Object,
                new AIRuntimeContextContributorCollection(() => []),
                new AIAgentScopeValidator(),
                new AIAgentSurfaceCollection(() => [new TestSurface()]));
        }

        public Mock<IAIAgentService> AgentService { get; } = new();

        public Mock<IAIAgentSelectionService> SelectionService { get; } = new();

        public StreamAgentAGUIController Controller { get; }

        public AIAgentSelectionInput? CapturedInput { get; private set; }

        public AIAgentExecutionOptions? CapturedOptions { get; private set; }

        public Guid? StreamedAgentId { get; private set; }
    }

    public static AGUIRunRequest CreateRunRequest(string? forwardedPropsJson = null)
        => new()
        {
            ThreadId = "thread",
            RunId = "run",
            Messages = [new AGUIMessage { Id = "m1", Role = AGUIMessageRole.User, Content = "hello" }],
            Context = [new AGUIContextItem { Description = "ctx", Value = "{}" }],
            ForwardedProps = forwardedPropsJson is null
                ? null
                : JsonDocument.Parse(forwardedPropsJson).RootElement.Clone(),
        };

    /// <summary>Executes an SSE result and returns the <c>value</c> of the first event.</summary>
    public static async Task<JsonElement> ReadFirstEventValueAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await result.ExecuteAsync(context);

        var text = Encoding.UTF8.GetString(body.ToArray());
        var firstData = text
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .First(chunk => chunk.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..];

        return JsonDocument.Parse(firstData).RootElement.GetProperty("value").Clone();
    }
}
