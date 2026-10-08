using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.AI.Core.Analytics;
using Umbraco.AI.Core.Analytics.Usage;

namespace Umbraco.AI.Tests.Unit.Analytics;

/// <summary>
/// #530: the first aggregation and rollup start from the oldest data, not the newest, and data an
/// earlier first run skipped is picked up again.
/// </summary>
public class AIUsageAggregationJobTests
{
    private static readonly DateTime CurrentHour = HourStart(DateTime.UtcNow);
    private static readonly DateTime Today = CurrentHour.Date;

    private readonly Mock<IAIUsageAggregationService> _aggregation = new();
    private readonly Mock<IAIUsageRecordRepository> _records = new();
    private readonly Mock<IAIUsageStatisticsRepository> _statistics = new();

    public AIUsageAggregationJobTests()
    {
        // By default every day asked for is ready to roll up.
        _aggregation.Setup(x => x.GetLastDayReadyForRollupAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime latestDay, CancellationToken _) => latestDay);
    }

    [Fact]
    public async Task HourlyJob_FirstRun_StartsFromTheOldestRecord()
    {
        // Arrange: no hourly statistics yet; records span CurrentHour-5 to now.
        _records.Setup(x => x.GetFirstRecordTimestampAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentHour.AddHours(-5).AddMinutes(20));

        // Act
        await CreateHourlyJob().RunJobAsync(CancellationToken.None);

        // Assert
        _aggregation.Verify(x => x.AggregateHourlyAsync(CurrentHour.AddHours(-5), It.IsAny<CancellationToken>()), Times.Once);
        _aggregation.Verify(x => x.AggregateHourlyAsync(CurrentHour.AddHours(-1), It.IsAny<CancellationToken>()), Times.Once);
        _aggregation.Verify(x => x.AggregateHourlyAsync(CurrentHour, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HourlyJob_AggregatesHoursAnEarlierFirstRunSkipped()
    {
        // Arrange: aggregation started at CurrentHour-10, leaving records behind at -14 and -12.
        // Each aggregation deletes its hour's records, so the oldest record moves forward.
        _statistics.Setup(x => x.GetFirstAggregatedHourlyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentHour.AddHours(-10));
        _statistics.Setup(x => x.GetLastAggregatedHourlyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentHour.AddHours(-1));
        _records.SetupSequence(x => x.GetFirstRecordTimestampAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentHour.AddHours(-14).AddMinutes(5))
            .ReturnsAsync(CurrentHour.AddHours(-12).AddMinutes(40))
            .ReturnsAsync(CurrentHour.AddMinutes(2));

        // Act
        await CreateHourlyJob().RunJobAsync(CancellationToken.None);

        // Assert
        _aggregation.Verify(x => x.AggregateHourlyAsync(CurrentHour.AddHours(-14), It.IsAny<CancellationToken>()), Times.Once);
        _aggregation.Verify(x => x.AggregateHourlyAsync(CurrentHour.AddHours(-12), It.IsAny<CancellationToken>()), Times.Once);
        _aggregation.Verify(x => x.AggregateHourlyAsync(It.Is<DateTime>(h => h > CurrentHour.AddHours(-12)), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HourlyJob_LeftoverRecordThatWontAggregate_DoesNotLoop()
    {
        // Arrange: the oldest record never moves (e.g. the aggregation keeps failing to delete it).
        _statistics.Setup(x => x.GetFirstAggregatedHourlyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentHour.AddHours(-10));
        _statistics.Setup(x => x.GetLastAggregatedHourlyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentHour.AddHours(-1));
        _records.Setup(x => x.GetFirstRecordTimestampAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentHour.AddHours(-14));

        // Act
        await CreateHourlyJob().RunJobAsync(CancellationToken.None);

        // Assert
        _aggregation.Verify(x => x.AggregateHourlyAsync(CurrentHour.AddHours(-14), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DailyJob_FirstRun_StartsFromTheOldestHourlyStatistic()
    {
        // Arrange
        _statistics.Setup(x => x.GetFirstAggregatedHourlyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddDays(-3).AddHours(7));
        _statistics.Setup(x => x.GetLastAggregatedHourlyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentHour.AddHours(-1));

        // Act
        await CreateDailyJob().RunJobAsync(CancellationToken.None);

        // Assert
        _aggregation.Verify(x => x.AggregateDailyAsync(Today.AddDays(-3), It.IsAny<CancellationToken>()), Times.Once);
        _aggregation.Verify(x => x.AggregateDailyAsync(Today.AddDays(-1), It.IsAny<CancellationToken>()), Times.Once);
        _aggregation.Verify(x => x.AggregateDailyAsync(Today, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DailyJob_RollsUpDaysAnEarlierFirstRunSkipped()
    {
        // Arrange: daily rollup started at Today-2, but hourly statistics go back to Today-5.
        _statistics.Setup(x => x.GetFirstAggregatedHourlyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddDays(-5).AddHours(9));
        _statistics.Setup(x => x.GetFirstAggregatedDailyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddDays(-2));
        _statistics.Setup(x => x.GetLastAggregatedDailyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddDays(-1));

        // Act
        await CreateDailyJob().RunJobAsync(CancellationToken.None);

        // Assert
        _aggregation.Verify(x => x.AggregateDailyAsync(Today.AddDays(-5), It.IsAny<CancellationToken>()), Times.Once);
        _aggregation.Verify(x => x.AggregateDailyAsync(Today.AddDays(-3), It.IsAny<CancellationToken>()), Times.Once);
        _aggregation.Verify(x => x.AggregateDailyAsync(Today.AddDays(-2), It.IsAny<CancellationToken>()), Times.Never);
        _aggregation.Verify(x => x.AggregateDailyAsync(Today.AddDays(-1), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DailyJob_StopsAtTheLastDayReadyForRollup()
    {
        // Arrange: yesterday is due, but the service says its hours aren't all aggregated yet.
        _statistics.Setup(x => x.GetFirstAggregatedHourlyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddDays(-3));
        _statistics.Setup(x => x.GetFirstAggregatedDailyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddDays(-3));
        _statistics.Setup(x => x.GetLastAggregatedDailyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddDays(-2));
        _aggregation.Setup(x => x.GetLastDayReadyForRollupAsync(Today.AddDays(-1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddDays(-2));

        // Act
        await CreateDailyJob().RunJobAsync(CancellationToken.None);

        // Assert
        _aggregation.Verify(x => x.AggregateDailyAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private AIUsageHourlyAggregationJob CreateHourlyJob() => new(
        _aggregation.Object,
        _records.Object,
        _statistics.Object,
        AnalyticsEnabled(),
        NullLogger<AIUsageHourlyAggregationJob>.Instance);

    private AIUsageDailyRollupJob CreateDailyJob() => new(
        _aggregation.Object,
        _statistics.Object,
        AnalyticsEnabled(),
        NullLogger<AIUsageDailyRollupJob>.Instance);

    private static IOptionsMonitor<AIAnalyticsOptions> AnalyticsEnabled()
    {
        var options = new Mock<IOptionsMonitor<AIAnalyticsOptions>>();
        options.Setup(x => x.CurrentValue).Returns(new AIAnalyticsOptions { Enabled = true });
        return options.Object;
    }

    private static DateTime HourStart(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc);
}
