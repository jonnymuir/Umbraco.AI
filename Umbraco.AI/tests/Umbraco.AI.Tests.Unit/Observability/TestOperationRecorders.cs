using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.AI.Core.Analytics;
using Umbraco.AI.Core.Analytics.Usage;
using Umbraco.AI.Core.Observability;

namespace Umbraco.AI.Tests.Unit.Observability;

/// <summary>
/// The tracker's default recorders, in their registered order, for tests that build an
/// <see cref="AIOperationTracker"/> by hand.
/// </summary>
internal static class TestOperationRecorders
{
    public static IAIOperationRecorder[] Default(
        IAIUsageRecordingService usageRecordingService,
        IAIUsageRecordFactory usageRecordFactory,
        IOptionsMonitor<AIAnalyticsOptions> analyticsOptions) =>
    [
        new AIAnalyticsOperationRecorder(
            usageRecordingService,
            usageRecordFactory,
            analyticsOptions,
            NullLogger<AIAnalyticsOperationRecorder>.Instance),
        new AITestUsageOperationRecorder(NullLogger<AITestUsageOperationRecorder>.Instance),
    ];
}
