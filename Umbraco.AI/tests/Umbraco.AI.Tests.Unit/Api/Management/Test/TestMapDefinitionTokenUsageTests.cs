// S3 — Compatible contract, persisted and exposed (AC3.2).
// Entry point: the real TestMapDefinition through UmbracoMapper, as in ChatMapDefinitionTests.
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Tests;
using Umbraco.AI.Web.Api.Management.Test.Mapping;
using Umbraco.AI.Web.Api.Management.Test.Models;
using Umbraco.Cms.Core.Mapping;
using Umbraco.Cms.Core.Scoping;
using Xunit;

namespace Umbraco.AI.Tests.Unit.Api.Management.Test;

public class TestMapDefinitionTokenUsageTests
{
    public class GivenARunWithPopulatedTokenUsage
    {
        private readonly AITestTokenUsageEntry _firstModel = new()
        {
            Capability = AICapability.Chat,
            ProviderId = "openai",
            ModelId = "gpt-4o",
            ProfileId = Guid.NewGuid(),
            ProfileAlias = "chat-profile",
            FeatureType = "inline-chat",
            FeatureId = Guid.NewGuid(),
            FeatureAlias = "guardrail-llm-evaluator",
            InputTokens = 10,
            OutputTokens = 5,
            TotalTokens = 15,
            CallCount = 2,
            UnreportedCallCount = 0
        };

        private readonly TestTokenUsageResponseModel _tokenUsage;

        public GivenARunWithPopulatedTokenUsage()
        {
            var mapper = new UmbracoMapper(
                new MapDefinitionCollection(() => new IMapDefinition[] { new TestMapDefinition() }),
                Mock.Of<ICoreScopeProvider>(),
                NullLogger<UmbracoMapper>.Instance);

            var run = new AITestRun
            {
                TestId = Guid.NewGuid(),
                Outcome = new AITestOutcome
                {
                    TokenUsage = new AITestTokenUsage
                    {
                        InputTokens = 14,
                        OutputTokens = 6,
                        TotalTokens = 20,
                        CallCount = 3,
                        UnreportedCallCount = 1,
                        Breakdown =
                        [
                            _firstModel,
                            new AITestTokenUsageEntry
                            {
                                Capability = AICapability.Embedding,
                                ModelId = "text-embedding-3-small",
                                CallCount = 1,
                                UnreportedCallCount = 1
                            }
                        ]
                    }
                }
            };

            var response = mapper.Map<TestRunResponseModel>(run)!;
            _tokenUsage = response.Outcome!.TokenUsage!;
        }

        [Fact]
        public void MapsTheCallCount() => _tokenUsage.CallCount.ShouldBe(3);

        [Fact]
        public void MapsTheUnreportedCallCount() => _tokenUsage.UnreportedCallCount.ShouldBe(1);

        [Fact]
        public void MapsTheBreakdownEntries() => _tokenUsage.Breakdown.Count().ShouldBe(2);

        [Fact]
        public void MapsTheEntryIdentity()
        {
            var first = _tokenUsage.Breakdown.First();

            (first.Capability, first.ProviderId, first.ModelId, first.ProfileId, first.ProfileAlias)
                .ShouldBe((nameof(AICapability.Chat), _firstModel.ProviderId, _firstModel.ModelId, _firstModel.ProfileId, _firstModel.ProfileAlias));
        }

        [Fact]
        public void MapsTheFeatureIdentity()
        {
            var first = _tokenUsage.Breakdown.First();

            (first.FeatureType, first.FeatureId, first.FeatureAlias)
                .ShouldBe((_firstModel.FeatureType, _firstModel.FeatureId, _firstModel.FeatureAlias));
        }
    }
}
