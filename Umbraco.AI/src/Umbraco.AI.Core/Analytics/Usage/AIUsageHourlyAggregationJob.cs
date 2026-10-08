using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Umbraco.AI.Core.Analytics.Usage;

/// <summary>
/// Recurring background job that aggregates raw usage records into hourly statistics.
/// Runs continuously, processing completed hours and catching up on any missed periods.
/// </summary>
internal sealed class AIUsageHourlyAggregationJob : RecurringBackgroundJobBase
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly IAIUsageAggregationService _aggregationService;
    private readonly IAIUsageRecordRepository _recordRepository;
    private readonly IAIUsageStatisticsRepository _statisticsRepository;
    private readonly IOptionsMonitor<AIAnalyticsOptions> _options;
    private readonly ILogger<AIUsageHourlyAggregationJob> _logger;

    public AIUsageHourlyAggregationJob(
        IAIUsageAggregationService aggregationService,
        IAIUsageRecordRepository recordRepository,
        IAIUsageStatisticsRepository statisticsRepository,
        IOptionsMonitor<AIAnalyticsOptions> options,
        ILogger<AIUsageHourlyAggregationJob> logger)
        : base(CheckInterval)
    {
        _aggregationService = aggregationService;
        _recordRepository = recordRepository;
        _statisticsRepository = statisticsRepository;
        _options = options;
        _logger = logger;
    }

    public override TimeSpan Delay => StartupDelay;

    public override async Task RunJobAsync(CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.Enabled)
        {
            _logger.LogDebug("Analytics disabled, skipping hourly aggregation");
            return;
        }

        await ProcessMissingHoursAsync(cancellationToken);
    }

    private async Task ProcessMissingHoursAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var currentCompletedHour = GetHourStart(now.AddHours(-1)); // Only process completed hours

        var lastAggregatedPeriod = await _statisticsRepository.GetLastAggregatedHourlyPeriodAsync(ct);

        DateTime startFromHour;

        if (lastAggregatedPeriod == null)
        {
            var firstRecordTimestamp = await _recordRepository.GetFirstRecordTimestampAsync(ct);

            if (firstRecordTimestamp == null)
            {
                _logger.LogDebug("No usage records found, nothing to aggregate");
                return;
            }

            startFromHour = GetHourStart(firstRecordTimestamp.Value);
            _logger.LogInformation(
                "First hourly aggregation: starting from {StartHour} (first record timestamp: {FirstRecord})",
                startFromHour,
                firstRecordTimestamp);
        }
        else
        {
            await AggregateSkippedHoursAsync(ct);

            startFromHour = lastAggregatedPeriod.Value.AddHours(1);
            _logger.LogDebug(
                "Last aggregated hour: {LastHour}, processing from {StartHour}",
                lastAggregatedPeriod,
                startFromHour);
        }

        var currentHour = startFromHour;
        var processedCount = 0;

        while (currentHour <= currentCompletedHour && !ct.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Aggregating hour: {Hour}", currentHour);
                await _aggregationService.AggregateHourlyAsync(currentHour, ct);
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to aggregate hour {Hour}, will retry on next run",
                    currentHour);

                break;
            }

            currentHour = currentHour.AddHours(1);
        }

        if (processedCount > 0)
        {
            _logger.LogInformation(
                "Processed {Count} hours from {Start} to {End}",
                processedCount,
                startFromHour,
                startFromHour.AddHours(processedCount - 1));
        }
        else if (startFromHour <= currentCompletedHour)
        {
            _logger.LogDebug("No new completed hours to process");
        }
    }

    /// <summary>
    /// Aggregates raw records older than the first hourly statistic. Earlier versions started the first run
    /// from the latest record instead of the earliest, which left those records behind for good. Those
    /// hours have no hourly statistics, so aggregating them can't overwrite anything. Each pass deletes the
    /// hour's records, so it walks only hours that hold records and stops once none are left.
    /// </summary>
    private async Task AggregateSkippedHoursAsync(CancellationToken ct)
    {
        var firstAggregatedPeriod = await _statisticsRepository.GetFirstAggregatedHourlyPeriodAsync(ct);
        if (firstAggregatedPeriod == null)
        {
            return;
        }

        DateTime? previousHour = null;

        while (!ct.IsCancellationRequested)
        {
            var firstRecordTimestamp = await _recordRepository.GetFirstRecordTimestampAsync(ct);
            if (firstRecordTimestamp == null)
            {
                return;
            }

            var hour = GetHourStart(firstRecordTimestamp.Value);
            if (hour >= firstAggregatedPeriod.Value || hour == previousHour)
            {
                return;
            }

            try
            {
                _logger.LogInformation("Aggregating skipped hour: {Hour}", hour);
                await _aggregationService.AggregateHourlyAsync(hour, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to aggregate skipped hour {Hour}, will retry on next run", hour);
                return;
            }

            previousHour = hour;
        }
    }

    private static DateTime GetHourStart(DateTime timestamp) => new(
        timestamp.Year,
        timestamp.Month,
        timestamp.Day,
        timestamp.Hour,
        0,
        0,
        DateTimeKind.Utc);
}
