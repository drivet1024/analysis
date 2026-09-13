using Microsoft.Extensions.Caching.Memory;
using MySqlConnector;

sealed record FloorScanDay(DateOnly Date, long Parcels, bool Partial);
sealed record FloorScanMonth(DateOnly Month, int DaysWithScans, int WeekdaysObserved, double? DailyScanRate);
sealed record FloorScanDepotRow(int DepotId, string DepotName, string DepotShortLabel,
    long SevenDayTotal, int WeekdaysWithoutScans, int WeekdaysObserved, DateTime? LatestScan,
    double? SixMonthDailyScanRate, double? PreviousThreeMonthRate, double? RecentThreeMonthRate,
    double? TrendChangePoints, string TrendDirection,
    IReadOnlyList<FloorScanDay> Days, IReadOnlyList<FloorScanMonth> Months);
sealed record FloorScanResponse(DateOnly Date, DateTime DatabaseNow, DateOnly SevenDayStart,
    DateOnly MonthStart, DateOnly MonthEnd, DateOnly TrendStart, DateOnly TrendEnd,
    double TrendThresholdPoints, bool CurrentDayPartial, IReadOnlyList<FloorScanDepotRow> Depots);

sealed class FloorScanService(DashboardConfig config)
{
    private sealed class DepotBuilder(int id, string name, string shortLabel)
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
        public string ShortLabel { get; } = shortLabel;
        public Dictionary<DateOnly, long> Daily { get; } = [];
        public DateTime? LatestScan { get; set; }
    }

    private const double TrendThresholdPoints = 2.0;
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 24 });
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<FloorScanResponse> GetAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var key = $"floor-scans:{date:yyyy-MM-dd}";
        if (cache.TryGetValue<FloorScanResponse>(key, out var hit)) return hit!;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<FloorScanResponse>(key, out hit)) return hit!;
            var now = DateTime.Now;
            var today = DateOnly.FromDateTime(now);
            var asOf = date == today ? now : date.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var monthStart = date.AddDays(-30);
            var chartStart = date.AddDays(-6);
            var trendStartCandidate = date.AddMonths(-5);
            var trendStart = new DateOnly(trendStartCandidate.Year, trendStartCandidate.Month, 1);
            var trendEnd = date.AddDays(-1);

            await using var connection = new MySqlConnection(config.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand("""
                WITH daily AS (
                  SELECT ph.DEPOT_ID depot_id,DATE(ph.DATE_LIV) scan_date,
                    COUNT(DISTINCT ph.PARCEL_ID) parcels,MAX(ph.DATE_LIV) latest_scan
                  FROM parcel_history ph
                  WHERE ph.EXCEPTION=904 AND COALESCE(ph.VOID,0)=0
                    AND ph.PARCEL_ID IS NOT NULL AND ph.PARCEL_ID<>0
                    AND ph.DATE_INSERT>=@queryStart-INTERVAL 1 DAY
                    AND ph.DATE_INSERT<@queryEnd+INTERVAL 1 DAY
                    AND ph.DATE_LIV>=@queryStart AND ph.DATE_LIV<@queryEnd
                  GROUP BY ph.DEPOT_ID,DATE(ph.DATE_LIV)
                )
                SELECT NOW() database_now,d.DEPOTNUMBER,d.DEPOTNAME,
                  COALESCE(NULLIF(TRIM(d.DEPOT_SHORT_LABEL),''),CONCAT('D',d.DEPOTNUMBER)) depot_short_label,
                  x.scan_date,x.parcels,x.latest_scan
                FROM depot d LEFT JOIN daily x ON x.depot_id=d.DEPOTNUMBER
                WHERE d.DEPOTNUMBER>0 AND d.DASHBOARD_ACTIVE=1
                  AND NULLIF(TRIM(d.DEPOTNAME),'') IS NOT NULL
                ORDER BY COALESCE(d.DASHBOARD_ORDER,9999),d.DEPOTNAME,x.scan_date
                """, connection) { CommandTimeout = 120 };
            command.Parameters.AddWithValue("@queryStart", monthStart.ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("@queryEnd", asOf);
            var depots = new List<DepotBuilder>();
            var byId = new Dictionary<int, DepotBuilder>();
            var databaseNow = now;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                databaseNow = reader.GetDateTime("database_now");
                var depotId = reader.GetInt32("DEPOTNUMBER");
                if (!byId.TryGetValue(depotId, out var depot))
                {
                    depot = new DepotBuilder(depotId, reader.GetString("DEPOTNAME").Trim(),
                        reader.GetString("depot_short_label").Trim());
                    byId.Add(depotId, depot);
                    depots.Add(depot);
                }
                if (reader.IsDBNull(reader.GetOrdinal("scan_date"))) continue;
                var scanDate = DateOnly.FromDateTime(reader.GetDateTime("scan_date"));
                depot.Daily[scanDate] = reader.GetInt64("parcels");
                var latestScan = reader.GetDateTime("latest_scan");
                if (depot.LatestScan is null || latestScan > depot.LatestScan) depot.LatestScan = latestScan;
            }

            var trendDaily = await GetTrendDailyAsync(trendStart, date, cancellationToken);

            var rows = depots.Select(depot =>
            {
                var days = Enumerable.Range(0, 7).Select(index => chartStart.AddDays(index))
                    .Select(day => new FloorScanDay(day, depot.Daily.GetValueOrDefault(day), day == today && date == today))
                    .ToArray();
                var workdays = Enumerable.Range(0, 30).Select(index => monthStart.AddDays(index))
                    .Where(day => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                    .ToArray();
                trendDaily.TryGetValue(depot.Id, out var depotTrendDaily);
                depotTrendDaily ??= [];
                var months = Enumerable.Range(0, 6).Select(index => trendStart.AddMonths(index))
                    .Select(month =>
                    {
                        var endExclusive = month.AddMonths(1) < date ? month.AddMonths(1) : date;
                        var observed = Enumerable.Range(0, Math.Max(0, endExclusive.DayNumber - month.DayNumber))
                            .Select(offset => month.AddDays(offset))
                            .Where(day => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                            .ToArray();
                        var scanned = observed.Count(day => depotTrendDaily.GetValueOrDefault(day) > 0);
                        return new FloorScanMonth(month, scanned, observed.Length,
                            observed.Length == 0 ? null : Math.Round(scanned * 100.0 / observed.Length, 1));
                    }).ToArray();
                var previousObserved = months.Take(3).Sum(month => month.WeekdaysObserved);
                var recentObserved = months.Skip(3).Sum(month => month.WeekdaysObserved);
                var allObserved = previousObserved + recentObserved;
                var previousScanned = months.Take(3).Sum(month => month.DaysWithScans);
                var recentScanned = months.Skip(3).Sum(month => month.DaysWithScans);
                var allScanned = previousScanned + recentScanned;
                double? sixMonthRate = allObserved == 0 ? null : Math.Round(allScanned * 100.0 / allObserved, 1);
                double? previousRate = previousObserved == 0 ? null : Math.Round(previousScanned * 100.0 / previousObserved, 1);
                double? recentRate = recentObserved == 0 ? null : Math.Round(recentScanned * 100.0 / recentObserved, 1);
                double? trendChange = previousRate is null || recentRate is null ? null
                    : Math.Round(recentRate.Value - previousRate.Value, 1);
                var trendDirection = trendChange switch
                {
                    >= TrendThresholdPoints => "positive",
                    <= -TrendThresholdPoints => "negative",
                    null => "unavailable",
                    _ => "stable"
                };
                return new FloorScanDepotRow(depot.Id, depot.Name, depot.ShortLabel,
                    days.Sum(day => day.Parcels), workdays.Count(day => depot.Daily.GetValueOrDefault(day) == 0),
                    workdays.Length, depot.LatestScan, sixMonthRate, previousRate, recentRate,
                    trendChange, trendDirection, days, months);
            }).ToArray();
            var result = new FloorScanResponse(date, databaseNow, chartStart, monthStart, date.AddDays(-1),
                trendStart, trendEnd, TrendThresholdPoints, date == today, rows);
            cache.Set(key, result, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
            return result;
        }
        finally { gate.Release(); }
    }

    private async Task<Dictionary<int, Dictionary<DateOnly, long>>> GetTrendDailyAsync(
        DateOnly trendStart, DateOnly endExclusive, CancellationToken cancellationToken)
    {
        var key = $"floor-scan-trend:{trendStart:yyyy-MM-dd}:{endExclusive:yyyy-MM-dd}";
        if (cache.TryGetValue<Dictionary<int, Dictionary<DateOnly, long>>>(key, out var hit)) return hit!;
        await using var connection = new MySqlConnection(config.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("""
            SELECT ph.DEPOT_ID depot_id,DATE(ph.DATE_LIV) scan_date,
              COUNT(DISTINCT ph.PARCEL_ID) parcels
            FROM parcel_history ph
            WHERE ph.EXCEPTION=904 AND COALESCE(ph.VOID,0)=0
              AND ph.PARCEL_ID IS NOT NULL AND ph.PARCEL_ID<>0
              AND ph.DATE_INSERT>=@trendStart-INTERVAL 1 DAY
              AND ph.DATE_INSERT<@trendEnd+INTERVAL 1 DAY
              AND ph.DATE_LIV>=@trendStart AND ph.DATE_LIV<@trendEnd
            GROUP BY ph.DEPOT_ID,DATE(ph.DATE_LIV)
            """, connection) { CommandTimeout = 180 };
        command.Parameters.AddWithValue("@trendStart", trendStart.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@trendEnd", endExclusive.ToDateTime(TimeOnly.MinValue));
        var result = new Dictionary<int, Dictionary<DateOnly, long>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var depotId = reader.GetInt32("depot_id");
            if (!result.TryGetValue(depotId, out var daily)) result[depotId] = daily = [];
            daily[DateOnly.FromDateTime(reader.GetDateTime("scan_date"))] = reader.GetInt64("parcels");
        }
        cache.Set(key, result, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6)
        });
        return result;
    }
}
