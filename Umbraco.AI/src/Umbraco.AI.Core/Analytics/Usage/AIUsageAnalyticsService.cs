using Microsoft.Extensions.Logging;

namespace Umbraco.AI.Core.Analytics.Usage;

/// <summary>
/// Service for querying aggregated AI usage statistics with hybrid live + historical data.
/// </summary>
internal sealed class AIUsageAnalyticsService : IAIUsageAnalyticsService
{
    private readonly IAIUsageRecordRepository _recordRepository;
    private readonly IAIUsageStatisticsRepository _statisticsRepository;
    private readonly ILogger<AIUsageAnalyticsService> _logger;

    public AIUsageAnalyticsService(
        IAIUsageRecordRepository recordRepository,
        IAIUsageStatisticsRepository statisticsRepository,
        ILogger<AIUsageAnalyticsService> logger)
    {
        _recordRepository = recordRepository;
        _statisticsRepository = statisticsRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AIUsageSummary> GetSummaryAsync(
        DateTime from,
        DateTime to,
        AIUsagePeriod? requestedGranularity = null,
        AIUsageFilter? filter = null,
        CancellationToken ct = default)
    {
        var granularity = DetermineGranularity(from, to, requestedGranularity);
        var statistics = await GetStatisticsAsync(from, to, granularity, filter, ct);

        var statsList = statistics.ToList();

        if (statsList.Count == 0)
        {
            return new AIUsageSummary
            {
                TotalRequests = 0,
                InputTokens = 0,
                OutputTokens = 0,
                TotalTokens = 0,
                SuccessCount = 0,
                FailureCount = 0,
                SuccessRate = 0,
                AverageDurationMs = 0
            };
        }

        var totalRequests = statsList.Sum(s => s.RequestCount);
        var successCount = statsList.Sum(s => s.SuccessCount);
        var failureCount = statsList.Sum(s => s.FailureCount);
        var totalDurationMs = statsList.Sum(s => s.TotalDurationMs);

        return new AIUsageSummary
        {
            TotalRequests = totalRequests,
            InputTokens = statsList.Sum(s => s.InputTokens),
            CachedInputTokens = AIUsageTokenAggregation.SumOrNull(statsList, s => s.CachedInputTokens),
            OutputTokens = statsList.Sum(s => s.OutputTokens),
            TotalTokens = statsList.Sum(s => s.TotalTokens),
            SuccessCount = successCount,
            FailureCount = failureCount,
            SuccessRate = totalRequests > 0 ? (double)successCount / totalRequests : 0,
            AverageDurationMs = totalRequests > 0 ? (int)(totalDurationMs / totalRequests) : 0
        };
    }

    /// <inheritdoc />
    public async Task<IEnumerable<AIUsageTimeSeriesPoint>> GetTimeSeriesAsync(
        DateTime from,
        DateTime to,
        AIUsagePeriod? requestedGranularity = null,
        AIUsageFilter? filter = null,
        CancellationToken ct = default)
    {
        var granularity = DetermineGranularity(from, to, requestedGranularity);
        var statistics = await GetStatisticsAsync(from, to, granularity, filter, ct);

        var timeSeries = statistics
            .GroupBy(s => s.Period)
            .Select(g => new AIUsageTimeSeriesPoint
            {
                Timestamp = g.Key,
                RequestCount = g.Sum(s => s.RequestCount),
                TotalTokens = g.Sum(s => s.TotalTokens),
                InputTokens = g.Sum(s => s.InputTokens),
                CachedInputTokens = AIUsageTokenAggregation.SumOrNull(g, s => s.CachedInputTokens),
                OutputTokens = g.Sum(s => s.OutputTokens),
                SuccessCount = g.Sum(s => s.SuccessCount),
                FailureCount = g.Sum(s => s.FailureCount)
            })
            .OrderBy(p => p.Timestamp)
            .ToList();

        return timeSeries;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<AIUsageBreakdownItem>> GetBreakdownByProviderAsync(
        DateTime from,
        DateTime to,
        AIUsagePeriod? requestedGranularity = null,
        CancellationToken ct = default)
    {
        var granularity = DetermineGranularity(from, to, requestedGranularity);
        var statistics = await GetStatisticsAsync(from, to, granularity, filter: null, ct);

        return CalculateBreakdown(
            statistics,
            s => s.ProviderId,
            null, // Providers don't have friendly names
            "Unknown Provider");
    }

    /// <inheritdoc />
    public async Task<IEnumerable<AIUsageBreakdownItem>> GetBreakdownByModelAsync(
        DateTime from,
        DateTime to,
        AIUsagePeriod? requestedGranularity = null,
        CancellationToken ct = default)
    {
        var granularity = DetermineGranularity(from, to, requestedGranularity);
        var statistics = await GetStatisticsAsync(from, to, granularity, filter: null, ct);

        return CalculateBreakdown(
            statistics,
            s => s.ModelId,
            null, // Models don't have friendly names
            "Unknown Model");
    }

    /// <inheritdoc />
    public async Task<IEnumerable<AIUsageBreakdownItem>> GetBreakdownByProfileAsync(
        DateTime from,
        DateTime to,
        AIUsagePeriod? requestedGranularity = null,
        CancellationToken ct = default)
    {
        var granularity = DetermineGranularity(from, to, requestedGranularity);
        var statistics = await GetStatisticsAsync(from, to, granularity, filter: null, ct);

        return CalculateBreakdown(
            statistics,
            s => s.ProfileId.ToString(),
            s => s.ProfileAlias, // Include profile alias as friendly name
            "Unknown Profile");
    }

    /// <inheritdoc />
    public async Task<IEnumerable<AIUsageBreakdownItem>> GetBreakdownByUserAsync(
        DateTime from,
        DateTime to,
        AIUsagePeriod? requestedGranularity = null,
        CancellationToken ct = default)
    {
        var granularity = DetermineGranularity(from, to, requestedGranularity);
        var statistics = await GetStatisticsAsync(from, to, granularity, filter: null, ct);

        return CalculateBreakdown(
            statistics,
            s => s.UserId ?? "Anonymous",
            s => s.UserName, // Include user name as friendly name
            "Anonymous");
    }

    /// <summary>
    /// Determines the appropriate granularity based on date range.
    /// </summary>
    private AIUsagePeriod DetermineGranularity(
        DateTime from,
        DateTime to,
        AIUsagePeriod? requested)
    {
        if (requested.HasValue)
            return requested.Value;

        var daySpan = (to - from).TotalDays;

        // Use hourly for up to 7 days, daily for longer periods
        return daySpan <= 7 ? AIUsagePeriod.Hourly : AIUsagePeriod.Daily;
    }

    /// <summary>
    /// Gets statistics from every layer that holds part of the range, split where the aggregation jobs
    /// have actually got to rather than at the clock: daily rows up to the last rolled-up day, hourly rows
    /// up to the last aggregated hour, then raw records. Each layer starts where the previous one ends, so
    /// nothing is missed while a job is behind and nothing is counted twice.
    /// </summary>
    private async Task<IEnumerable<AIUsageStatistics>> GetStatisticsAsync(
        DateTime from,
        DateTime to,
        AIUsagePeriod granularity,
        AIUsageFilter? filter,
        CancellationToken ct)
    {
        var periodStart = granularity == AIUsagePeriod.Hourly
            ? (Func<DateTime, DateTime>)GetHourStart
            : GetDayStart;

        // Raw records are deleted once their hour is aggregated, so they hold everything after the last
        // aggregated hour. With no hourly statistics yet, everything is still raw.
        var lastHourlyPeriod = await _statisticsRepository.GetLastAggregatedHourlyPeriodAsync(ct);
        var rawFrom = lastHourlyPeriod?.AddHours(1) ?? from;

        var allStats = new List<AIUsageStatistics>();

        if (granularity == AIUsagePeriod.Hourly)
        {
            allStats.AddRange(await GetHourlyStatisticsAsync(from, Earliest(to, rawFrom), filter, ct));
        }
        else
        {
            // Daily rows only exist for days the rollup job has completed; later days are still hourly.
            var lastDailyPeriod = await _statisticsRepository.GetLastAggregatedDailyPeriodAsync(ct);
            var hourlyFrom = lastDailyPeriod?.AddDays(1) ?? from;

            allStats.AddRange(await GetDailyStatisticsAsync(from, Earliest(to, hourlyFrom), filter, ct));

            var hourlyStats = await GetHourlyStatisticsAsync(
                Latest(from, hourlyFrom), Earliest(to, rawFrom), filter, ct);
            allStats.AddRange(RollUp(hourlyStats, periodStart));
        }

        try
        {
            var liveStats = await GetLiveStatisticsAsync(
                Latest(from, rawFrom),
                Earliest(to, DateTime.UtcNow),
                periodStart,
                filter,
                ct);

            if (liveStats != null)
            {
                allStats.AddRange(liveStats);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get live statistics, using aggregated data only");
        }

        return allStats;
    }

    private async Task<IEnumerable<AIUsageStatistics>> GetHourlyStatisticsAsync(
        DateTime from,
        DateTime to,
        AIUsageFilter? filter,
        CancellationToken ct)
        => from < to ? await _statisticsRepository.GetHourlyByPeriodAsync(from, to, filter, ct) : [];

    private async Task<IEnumerable<AIUsageStatistics>> GetDailyStatisticsAsync(
        DateTime from,
        DateTime to,
        AIUsageFilter? filter,
        CancellationToken ct)
        => from < to ? await _statisticsRepository.GetDailyByPeriodAsync(from, to, filter, ct) : [];

    /// <summary>
    /// Re-buckets statistics into coarser periods, e.g. hourly rows into days.
    /// </summary>
    private static IEnumerable<AIUsageStatistics> RollUp(
        IEnumerable<AIUsageStatistics> statistics,
        Func<DateTime, DateTime> periodStart)
        => statistics
            .GroupBy(s => new
            {
                Period = periodStart(s.Period),
                s.ProviderId,
                s.ModelId,
                s.ProfileId,
                s.ProfileAlias,
                s.Capability,
                s.UserId,
                s.UserName,
                s.EntityType,
                s.FeatureType
            })
            .Select(g => new AIUsageStatistics
            {
                Id = Guid.NewGuid(),
                Period = g.Key.Period,
                ProviderId = g.Key.ProviderId,
                ModelId = g.Key.ModelId,
                ProfileId = g.Key.ProfileId,
                ProfileAlias = g.Key.ProfileAlias,
                Capability = g.Key.Capability,
                UserId = g.Key.UserId,
                UserName = g.Key.UserName,
                EntityType = g.Key.EntityType,
                FeatureType = g.Key.FeatureType,
                RequestCount = g.Sum(s => s.RequestCount),
                SuccessCount = g.Sum(s => s.SuccessCount),
                FailureCount = g.Sum(s => s.FailureCount),
                InputTokens = g.Sum(s => s.InputTokens),
                CachedInputTokens = AIUsageTokenAggregation.SumOrNull(g, s => s.CachedInputTokens),
                OutputTokens = g.Sum(s => s.OutputTokens),
                TotalTokens = g.Sum(s => s.TotalTokens),
                TotalDurationMs = g.Sum(s => s.TotalDurationMs),
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

    private static DateTime Earliest(DateTime a, DateTime b) => a < b ? a : b;

    private static DateTime Latest(DateTime a, DateTime b) => a > b ? a : b;

    /// <summary>
    /// Gets live statistics from raw usage records that have not been aggregated yet, bucketed into the
    /// requested granularity. Aggregates in-memory to match statistics format.
    /// </summary>
    private async Task<IEnumerable<AIUsageStatistics>?> GetLiveStatisticsAsync(
        DateTime from,
        DateTime to,
        Func<DateTime, DateTime> periodStart,
        AIUsageFilter? filter,
        CancellationToken ct)
    {
        if (from >= to)
            return null;

        var records = await _recordRepository.GetRecordsByPeriodAsync(from, to, ct);
        var recordList = records.ToList();

        if (recordList.Count == 0)
            return null;

        // Apply filter if specified
        if (filter != null)
        {
            recordList = ApplyFilterToRecords(recordList, filter);
        }

        if (recordList.Count == 0)
            return null;

        // Aggregate in-memory, grouped by dimensions
        var aggregated = recordList
            .GroupBy(r => new
            {
                Period = periodStart(r.Timestamp),
                r.ProviderId,
                r.ModelId,
                r.ProfileId,
                r.ProfileAlias,
                r.Capability,
                r.UserId,
                r.UserName,
                r.EntityType,
                r.FeatureType
            })
            .Select(g => new AIUsageStatistics
            {
                Id = Guid.NewGuid(),
                Period = g.Key.Period,
                ProviderId = g.Key.ProviderId,
                ModelId = g.Key.ModelId,
                ProfileId = g.Key.ProfileId,
                ProfileAlias = g.Key.ProfileAlias,
                Capability = g.Key.Capability,
                UserId = g.Key.UserId,
                UserName = g.Key.UserName,
                EntityType = g.Key.EntityType,
                FeatureType = g.Key.FeatureType,
                RequestCount = g.Count(),
                SuccessCount = g.Count(r => r.Status == AIUsageRecordStatus.Succeeded),
                FailureCount = g.Count(r => r.Status is AIUsageRecordStatus.Failed or AIUsageRecordStatus.Blocked),
                InputTokens = g.Sum(r => (long)r.InputTokens),
                CachedInputTokens = AIUsageTokenAggregation.SumOrNull(g, r => r.CachedInputTokens),
                OutputTokens = g.Sum(r => (long)r.OutputTokens),
                TotalTokens = g.Sum(r => (long)r.TotalTokens),
                TotalDurationMs = g.Sum(r => r.DurationMs),
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

        _logger.LogDebug(
            "Aggregated {RecordCount} live records into {StatsCount} statistics groups",
            recordList.Count,
            aggregated.Count);

        return aggregated;
    }

    /// <summary>
    /// Applies filter to raw usage records.
    /// </summary>
    private static List<AIUsageRecord> ApplyFilterToRecords(
        List<AIUsageRecord> records,
        AIUsageFilter filter)
    {
        var filtered = records.AsEnumerable();

        if (filter.ProviderId != null)
            filtered = filtered.Where(r => r.ProviderId == filter.ProviderId);

        if (filter.ModelId != null)
            filtered = filtered.Where(r => r.ModelId == filter.ModelId);

        if (filter.ProfileId != null)
            filtered = filtered.Where(r => r.ProfileId == filter.ProfileId.Value);

        if (filter.Capability != null)
            filtered = filtered.Where(r => r.Capability == filter.Capability.Value);

        if (filter.UserId != null)
            filtered = filtered.Where(r => r.UserId == filter.UserId);

        if (filter.EntityType != null)
            filtered = filtered.Where(r => r.EntityType == filter.EntityType);

        if (filter.FeatureType != null)
            filtered = filtered.Where(r => r.FeatureType == filter.FeatureType);

        return filtered.ToList();
    }

    /// <summary>
    /// Calculates breakdown by a specific dimension with percentages.
    /// </summary>
    private static IEnumerable<AIUsageBreakdownItem> CalculateBreakdown(
        IEnumerable<AIUsageStatistics> statistics,
        Func<AIUsageStatistics, string> dimensionSelector,
        Func<AIUsageStatistics, string?>? nameSelector,
        string unknownLabel)
    {
        var statsList = statistics.ToList();

        if (statsList.Count == 0)
            return [];

        var totalRequests = statsList.Sum(s => s.RequestCount);

        var breakdown = statsList
            .GroupBy(s => new
            {
                Dimension = dimensionSelector(s),
                Name = nameSelector?.Invoke(s)
            })
            .Select(g => new AIUsageBreakdownItem
            {
                Dimension = string.IsNullOrEmpty(g.Key.Dimension) ? unknownLabel : g.Key.Dimension,
                DimensionName = g.Key.Name,
                RequestCount = g.Sum(s => s.RequestCount),
                TotalTokens = g.Sum(s => s.TotalTokens),
                CachedInputTokens = AIUsageTokenAggregation.SumOrNull(g, s => s.CachedInputTokens),
                Percentage = totalRequests > 0
                    ? (double)g.Sum(s => s.RequestCount) / totalRequests * 100
                    : 0
            })
            .OrderByDescending(b => b.RequestCount)
            .ToList();

        return breakdown;
    }

    /// <summary>
    /// Gets the start of the hour for a given timestamp.
    /// </summary>
    private static DateTime GetHourStart(DateTime timestamp)
    {
        return new DateTime(
            timestamp.Year,
            timestamp.Month,
            timestamp.Day,
            timestamp.Hour,
            0,
            0,
            DateTimeKind.Utc);
    }

    /// <summary>
    /// Gets the start of the day (midnight UTC) for a given timestamp.
    /// </summary>
    private static DateTime GetDayStart(DateTime timestamp)
    {
        return new DateTime(
            timestamp.Year,
            timestamp.Month,
            timestamp.Day,
            0,
            0,
            0,
            DateTimeKind.Utc);
    }
}
