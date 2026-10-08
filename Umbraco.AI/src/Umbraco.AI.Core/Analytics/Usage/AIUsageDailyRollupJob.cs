using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Umbraco.AI.Core.Analytics.Usage;

/// <summary>
/// Recurring background job that rolls up hourly statistics into daily statistics.
/// Runs hourly, processing completed days and catching up on any missed periods.
/// </summary>
internal sealed class AIUsageDailyRollupJob : RecurringBackgroundJobBase
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IAIUsageAggregationService _aggregationService;
    private readonly IAIUsageStatisticsRepository _statisticsRepository;
    private readonly IOptionsMonitor<AIAnalyticsOptions> _options;
    private readonly ILogger<AIUsageDailyRollupJob> _logger;

    public AIUsageDailyRollupJob(
        IAIUsageAggregationService aggregationService,
        IAIUsageStatisticsRepository statisticsRepository,
        IOptionsMonitor<AIAnalyticsOptions> options,
        ILogger<AIUsageDailyRollupJob> logger)
        : base(CheckInterval)
    {
        _aggregationService = aggregationService;
        _statisticsRepository = statisticsRepository;
        _options = options;
        _logger = logger;
    }

    public override TimeSpan Delay => StartupDelay;

    public override async Task RunJobAsync(CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.Enabled)
        {
            _logger.LogDebug("Analytics disabled, skipping daily rollup");
            return;
        }

        await ProcessMissingDaysAsync(cancellationToken);
    }

    private async Task ProcessMissingDaysAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var yesterday = GetDayStart(now.AddDays(-1)); // Only process completed days (yesterday and earlier)

        // A day is rolled up from its hourly statistics, so it must wait until the hourly job has
        // aggregated all its hours, or its daily total would leave them out for good.
        var lastReadyDay = await _aggregationService.GetLastDayReadyForRollupAsync(yesterday, ct);

        var lastAggregatedPeriod = await _statisticsRepository.GetLastAggregatedDailyPeriodAsync(ct);

        DateTime startFromDay;

        var firstHourlyPeriod = await _statisticsRepository.GetFirstAggregatedHourlyPeriodAsync(ct);

        if (lastAggregatedPeriod == null)
        {
            if (firstHourlyPeriod == null)
            {
                _logger.LogDebug("No hourly statistics found, nothing to roll up into daily");
                return;
            }

            startFromDay = GetDayStart(firstHourlyPeriod.Value);
            _logger.LogInformation(
                "First daily rollup: starting from {StartDay} (first hourly stat: {FirstHourly})",
                startFromDay,
                firstHourlyPeriod);
        }
        else
        {
            // Earlier versions started the first rollup from the latest hourly statistic, skipping the days
            // before it. Those days have no daily statistics, so rolling them up can't overwrite anything.
            var firstDailyPeriod = await _statisticsRepository.GetFirstAggregatedDailyPeriodAsync(ct);
            if (firstHourlyPeriod != null && firstDailyPeriod != null)
            {
                await RollUpDaysAsync(GetDayStart(firstHourlyPeriod.Value), Min(firstDailyPeriod.Value.AddDays(-1), lastReadyDay), ct);
            }

            startFromDay = lastAggregatedPeriod.Value.AddDays(1);
            _logger.LogDebug(
                "Last aggregated day: {LastDay}, processing from {StartDay}",
                lastAggregatedPeriod,
                startFromDay);
        }

        if (startFromDay > lastReadyDay)
        {
            _logger.LogDebug("No completed days to process");
            return;
        }

        await RollUpDaysAsync(startFromDay, lastReadyDay, ct);
    }

    /// <summary>
    /// Rolls up each day from <paramref name="firstDay"/> to <paramref name="lastDay"/> inclusive, stopping
    /// at the first failure so the next run retries from there.
    /// </summary>
    private async Task RollUpDaysAsync(DateTime firstDay, DateTime lastDay, CancellationToken ct)
    {
        var currentDay = firstDay;
        var processedCount = 0;

        while (currentDay <= lastDay && !ct.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Rolling up daily statistics for: {Day}", currentDay);
                await _aggregationService.AggregateDailyAsync(currentDay, ct);
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to roll up day {Day}, will retry on next run",
                    currentDay);

                break;
            }

            currentDay = currentDay.AddDays(1);
        }

        if (processedCount > 0)
        {
            _logger.LogInformation(
                "Processed {Count} days from {Start} to {End}",
                processedCount,
                firstDay,
                firstDay.AddDays(processedCount - 1));
        }
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static DateTime GetDayStart(DateTime timestamp) => new(
        timestamp.Year,
        timestamp.Month,
        timestamp.Day,
        0,
        0,
        0,
        DateTimeKind.Utc);
}
