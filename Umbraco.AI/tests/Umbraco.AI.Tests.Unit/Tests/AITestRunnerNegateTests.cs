using System.Text.Json;
using Umbraco.AI.Core.EditableModels;
using Umbraco.AI.Core.Tests;

namespace Umbraco.AI.Tests.Unit.Tests;

/// <summary>
/// Verifies <see cref="AITestRunner"/> only inverts real grader verdicts when
/// <see cref="AITestGraderConfig.Negate"/> is set. Regression test for umbraco/Umbraco.AI#429:
/// graders report their own errors as failed results, and negation used to flip those into passes.
/// </summary>
public class AITestRunnerNegateTests
{
    private readonly Mock<IAITestRunRepository> _runRepositoryMock = new();
    private readonly Mock<IAITestTranscriptRepository> _transcriptRepositoryMock = new();
    private readonly List<AITestRun> _savedRuns = [];

    public AITestRunnerNegateTests()
    {
        _runRepositoryMock
            .Setup(x => x.SaveAsync(It.IsAny<AITestRun>(), It.IsAny<CancellationToken>()))
            .Callback<AITestRun, CancellationToken>((run, _) => _savedRuns.Add(run))
            .ReturnsAsync((AITestRun run, CancellationToken _) => run);
    }

    private AITestRunner CreateRunner(params IAITestGrader[] graders) => new(
        _runRepositoryMock.Object,
        _transcriptRepositoryMock.Object,
        new AITestFeatureCollection(() => [new StubTestFeature()]),
        new AITestGraderCollection(() => graders));

    private static AITest BuildTest(bool negate, string graderTypeId = StubGrader.GraderId) => new()
    {
        Id = Guid.NewGuid(),
        Alias = "t",
        Name = "t",
        TestFeatureId = StubTestFeature.FeatureId,
        TestTargetId = Guid.NewGuid(),
        RunCount = 1,
        Graders =
        [
            new AITestGraderConfig { GraderTypeId = graderTypeId, Name = "g", Negate = negate },
        ],
    };

    private async Task<AITestRun> RunAsync(AITest test, params IAITestGrader[] graders)
    {
        await CreateRunner(graders).ExecuteTestAsync(test);
        return _savedRuns.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ExecuteTestAsync_NegatedRealFailure_Passes()
    {
        var run = await RunAsync(BuildTest(negate: true), new StubGrader(passed: false, isError: false));

        run.Status.ShouldBe(AITestRunStatus.Passed);
        run.GraderResults.ShouldHaveSingleItem().Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteTestAsync_NegatedRealPass_Fails()
    {
        var run = await RunAsync(BuildTest(negate: true), new StubGrader(passed: true, isError: false));

        run.Status.ShouldBe(AITestRunStatus.Failed);
    }

    [Fact]
    public async Task ExecuteTestAsync_NegatedErrorResult_StillFails()
    {
        var run = await RunAsync(BuildTest(negate: true), new StubGrader(passed: false, isError: true));

        run.Status.ShouldBe(AITestRunStatus.Failed);
        var result = run.GraderResults.ShouldHaveSingleItem();
        result.Passed.ShouldBeFalse();
        result.Score.ShouldBe(0.0);
        result.FailureMessage.ShouldBe(StubGrader.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteTestAsync_ErrorResultClaimingPass_Fails()
    {
        // An error carries no verdict, so a grader that sets IsError can't also report a pass.
        var run = await RunAsync(BuildTest(negate: false), new StubGrader(passed: true, isError: true));

        run.Status.ShouldBe(AITestRunStatus.Failed);
    }

    [Fact]
    public async Task ExecuteTestAsync_NegatedGraderThatThrows_Fails()
    {
        var run = await RunAsync(BuildTest(negate: true), new StubGrader(passed: false, isError: false, @throw: true));

        run.Status.ShouldBe(AITestRunStatus.Failed);
        run.GraderResults.ShouldHaveSingleItem().IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteTestAsync_NegatedMissingGrader_Fails()
    {
        var run = await RunAsync(BuildTest(negate: true, graderTypeId: "missing"));

        run.Status.ShouldBe(AITestRunStatus.Failed);
        run.GraderResults.ShouldHaveSingleItem().IsError.ShouldBeTrue();
    }

    private sealed class StubGrader(bool passed, bool isError, bool @throw = false) : IAITestGrader
    {
        public const string GraderId = "stub";
        public const string ErrorMessage = "Grader could not run";

        public string Id => GraderId;
        public string Name => "Stub";
        public string Description => "Returns a fixed result";
        public AIGraderType Type => AIGraderType.CodeBased;
        public Type? ConfigType => null;
        public AIEditableModelSchema? GetConfigSchema() => null;

        public Task<AITestGraderResult> GradeAsync(
            AITestTranscript transcript,
            AITestOutcome outcome,
            AITestGraderConfig graderConfig,
            CancellationToken cancellationToken)
        {
            if (@throw)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.FromResult(new AITestGraderResult
            {
                GraderId = graderConfig.Id,
                Passed = passed,
                Score = passed ? 1.0 : 0.0,
                FailureMessage = isError ? ErrorMessage : null,
                IsError = isError,
            });
        }
    }

    private sealed class StubTestFeature : IAITestFeature
    {
        public const string FeatureId = "stub";

        public string Id => FeatureId;
        public string Name => "Stub";
        public string Description => "Returns an empty transcript";
        public string Category => "test";
        public Type? ConfigType => null;
        public AIEditableModelSchema? GetConfigSchema() => null;
        public string ExtractOutputValue(AITestTranscript transcript) => string.Empty;

        public Task<AITestTranscript> ExecuteAsync(
            AITest test,
            int runNumber,
            Guid? profileIdOverride,
            IEnumerable<Guid>? contextIdsOverride,
            IEnumerable<Guid>? guardrailIdsOverride,
            CancellationToken cancellationToken)
            => Task.FromResult(new AITestTranscript
            {
                RunId = Guid.Empty,
                FinalOutput = JsonDocument.Parse("{}").RootElement,
            });
    }
}
