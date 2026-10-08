using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.AI.Core.Analytics;
using Umbraco.AI.Core.Analytics.Usage;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Runtime;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;

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
    public async Task DailyJob_WaitsForADayWhoseHoursAreNotAllAggregated()
    {
        // Arrange: yesterday's last hour still has raw records (the hourly job hasn't reached it), and
        // the daily rollup is due for yesterday. Rolling it up now would leave that hour out for good.
        _statistics.Setup(x => x.GetFirstAggregatedHourlyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddDays(-3));
        _statistics.Setup(x => x.GetFirstAggregatedDailyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddDays(-3));
        _statistics.Setup(x => x.GetLastAggregatedDailyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddDays(-2));
        _records.Setup(x => x.GetFirstRecordTimestampAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddHours(-1).AddMinutes(30));

        // Act
        await CreateDailyJob().RunJobAsync(CancellationToken.None);

        // Assert
        _aggregation.Verify(x => x.AggregateDailyAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DailyJob_RollsUpADayOnceAllItsHoursAreAggregated()
    {
        // Arrange: the only raw records left are from today, so yesterday is fully in hourly statistics.
        _statistics.Setup(x => x.GetFirstAggregatedHourlyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddDays(-3));
        _statistics.Setup(x => x.GetFirstAggregatedDailyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddDays(-3));
        _statistics.Setup(x => x.GetLastAggregatedDailyPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddDays(-2));
        _records.Setup(x => x.GetFirstRecordTimestampAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Today.AddMinutes(10));

        // Act
        await CreateDailyJob().RunJobAsync(CancellationToken.None);

        // Assert
        _aggregation.Verify(x => x.AggregateDailyAsync(Today.AddDays(-1), It.IsAny<CancellationToken>()), Times.Once);
    }

    private TestHourlyJob CreateHourlyJob() => new(new AIUsageHourlyAggregationJob(
        _aggregation.Object,
        _records.Object,
        _statistics.Object,
        AnalyticsEnabled(),
        RunningRuntime(),
        SingleServer(),
        MainDom(),
        NullLogger<AIUsageHourlyAggregationJob>.Instance));

    private TestDailyJob CreateDailyJob() => new(new AIUsageDailyRollupJob(
        _aggregation.Object,
        _records.Object,
        _statistics.Object,
        AnalyticsEnabled(),
        RunningRuntime(),
        SingleServer(),
        MainDom(),
        NullLogger<AIUsageDailyRollupJob>.Instance));

    // v17's jobs are hosted services; these give them the same RunJobAsync shape the tests use on v18.
    private sealed class TestHourlyJob(AIUsageHourlyAggregationJob job)
    {
        public Task RunJobAsync(CancellationToken _) => job.PerformExecuteAsync(null);
    }

    private sealed class TestDailyJob(AIUsageDailyRollupJob job)
    {
        public Task RunJobAsync(CancellationToken _) => job.PerformExecuteAsync(null);
    }

    private static IRuntimeState RunningRuntime()
    {
        var runtime = new Mock<IRuntimeState>();
        runtime.Setup(x => x.Level).Returns(RuntimeLevel.Run);
        return runtime.Object;
    }

    private static IServerRoleAccessor SingleServer()
    {
        var accessor = new Mock<IServerRoleAccessor>();
        accessor.Setup(x => x.CurrentServerRole).Returns(ServerRole.Single);
        return accessor.Object;
    }

    private static IMainDom MainDom()
    {
        var mainDom = new Mock<IMainDom>();
        mainDom.Setup(x => x.IsMainDom).Returns(true);
        return mainDom.Object;
    }

    private static IOptionsMonitor<AIAnalyticsOptions> AnalyticsEnabled()
    {
        var options = new Mock<IOptionsMonitor<AIAnalyticsOptions>>();
        options.Setup(x => x.CurrentValue).Returns(new AIAnalyticsOptions { Enabled = true });
        return options.Object;
    }

    private static DateTime HourStart(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc);
}
