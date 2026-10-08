// S1 - Plug in my own agent selection rule (AC9 obsolete method, AC10 builder registration)
// AC10 resolves the collection through a real IServiceProvider, using the builder's own RegisterWith,
// exactly as Umbraco's composer does. The full AddUmbracoAIAgentCore pipeline can't run outside a
// CMS host (TypeLoader), so "LLMAgentSelector is in the default registrations" is checked on the demo site in T11.
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;
using Umbraco.AI.Agent.Core.Agents;
using Umbraco.AI.Agent.Core.Agents.Selection;
using Umbraco.AI.Core.Chat;
using Umbraco.AI.Core.Profiles;
using Xunit;
using static Umbraco.AI.Agent.Tests.Unit.Agents.Selection.AgentSelectionTestBuilders;
using UmbracoAIAgent = Umbraco.AI.Agent.Core.Agents.AIAgent;

namespace Umbraco.AI.Agent.Tests.Unit.Agents.Selection;

public class AgentSelectionRegistrationTests
{
    private static readonly UmbracoAIAgent AgentB = CreateAgent(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), "agent-b");

    private sealed class MySelector : IAIAgentSelector
    {
        public Task<AIAgentSelectionResult?> SelectAgentAsync(AIAgentSelectionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<AIAgentSelectionResult?>(null);
    }

    // ---- Happy path --------------------------------------------------------------------------

    // AC9 - Obsolete method still works
    [Collection(StaticServiceProviderCollection.Name)]
    public class GivenTheSelectionServicePicksAgentB : IDisposable
    {
        private readonly AgentServiceHarness _harness;
        private readonly UmbracoAIAgent? _result;

        public GivenTheSelectionServicePicksAgentB()
        {
            var selectionService = new Mock<IAIAgentSelectionService>();
            selectionService
                .Setup(x => x.SelectAgentAsync(It.IsAny<AIAgentSelectionInput>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AIAgentSelectionResult(AgentB, "custom", null));

            _harness = new AgentServiceHarness(AgentB, selectionService.Object);

#pragma warning disable CS0618 // Exercising the obsolete proxy on purpose
            _result = _harness.Service
                .SelectAgentForPromptAsync("hello", SurfaceId, CreateAvailabilityContext())
                .GetAwaiter().GetResult();
#pragma warning restore CS0618
        }

        public void Dispose() => _harness.Dispose();

        [Fact]
        public void ReturnsTheAgentTheNewServiceSelected() => _result.ShouldBe(AgentB);
    }

    // AC10 - Registered through the builder
    public class GivenACustomSelectorInsertedBeforeTheLLMSelector
    {
        private readonly AIAgentSelectorCollection _collection;

        public GivenACustomSelectorInsertedBeforeTheLLMSelector()
        {
            var builder = new AIAgentSelectorCollectionBuilder()
                .Append<LLMAgentSelector>()
                .InsertBefore<LLMAgentSelector, MySelector>();

            var services = new ServiceCollection();
            services.AddSingleton(Mock.Of<IAIProfileService>());
            services.AddSingleton(Mock.Of<IAIChatClientFactory>());
            builder.RegisterWith(services);

            _collection = services.BuildServiceProvider().GetRequiredService<AIAgentSelectorCollection>();
        }

        [Fact]
        public void TheCustomSelectorRunsFirst()
            => _collection.Select(s => s.GetType()).ShouldBe([typeof(MySelector), typeof(LLMAgentSelector)]);
    }
}
