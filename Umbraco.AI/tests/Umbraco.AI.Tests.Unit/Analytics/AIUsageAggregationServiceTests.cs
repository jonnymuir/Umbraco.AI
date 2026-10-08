using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.AI.Core.Analytics.Usage;

namespace Umbraco.AI.Tests.Unit.Analytics;

/// <summary>
/// A day is only ready to roll up once the hourly aggregation has reached all its hours, which it
/// shows by deleting their raw records.
/// </summary>
public class AIUsageAggregationServiceTests
{
    private static readonly DateTime Yesterday = DateTime.UtcNow.Date.AddDays(-1);

    private readonly Mock<IAIUsageRecordRepository> _records = new();

    [Fact]
    public async Task GetLastDayReadyForRollupAsync_WithNoRawRecords_ReturnsTheLatestDayAsked()
    {
        var result = await CreateService().GetLastDayReadyForRollupAsync(Yesterday);

        result.ShouldBe(Yesterday);
    }

    [Fact]
    public async Task GetLastDayReadyForRollupAsync_WithRawRecordsOnADay_ReturnsTheDayBefore()
    {
        // Yesterday's last hour still has raw records: the hourly aggregation hasn't reached it.
        _records.Setup(x => x.GetFirstRecordTimestampAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Yesterday.AddHours(23).AddMinutes(30));

        var result = await CreateService().GetLastDayReadyForRollupAsync(Yesterday);

        result.ShouldBe(Yesterday.AddDays(-1));
    }

    [Fact]
    public async Task GetLastDayReadyForRollupAsync_WithRawRecordsOnlyAfterTheDay_ReturnsTheLatestDayAsked()
    {
        _records.Setup(x => x.GetFirstRecordTimestampAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Yesterday.AddDays(1).AddMinutes(10));

        var result = await CreateService().GetLastDayReadyForRollupAsync(Yesterday);

        result.ShouldBe(Yesterday);
    }

    private AIUsageAggregationService CreateService() => new(
        _records.Object,
        Mock.Of<IAIUsageStatisticsRepository>(),
        NullLogger<AIUsageAggregationService>.Instance);
}
