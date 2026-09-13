using Microsoft.Extensions.Caching.Memory;
using MySqlConnector;

sealed record FloorScanDay(DateOnly Date, long Parcels, bool Partial);
sealed record FloorScanDepotRow(int DepotId, string DepotName, string DepotShortLabel,
    long SevenDayTotal, int WeekdaysWithoutScans, int WeekdaysObserved, DateTime? LatestScan,
    IReadOnlyList<FloorScanDay> Days);
sealed record FloorScanResponse(DateOnly Date, DateTime DatabaseNow, DateOnly SevenDayStart,
    DateOnly MonthStart, DateOnly MonthEnd, bool CurrentDayPartial, IReadOnlyList<FloorScanDepotRow> Depots);

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

    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 8 });
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

            var rows = depots.Select(depot =>
            {
                var days = Enumerable.Range(0, 7).Select(index => chartStart.AddDays(index))
                    .Select(day => new FloorScanDay(day, depot.Daily.GetValueOrDefault(day), day == today && date == today))
                    .ToArray();
                var workdays = Enumerable.Range(0, 30).Select(index => monthStart.AddDays(index))
                    .Where(day => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                    .ToArray();
                return new FloorScanDepotRow(depot.Id, depot.Name, depot.ShortLabel,
                    days.Sum(day => day.Parcels), workdays.Count(day => depot.Daily.GetValueOrDefault(day) == 0),
                    workdays.Length, depot.LatestScan, days);
            }).ToArray();
            var result = new FloorScanResponse(date, databaseNow, chartStart, monthStart, date.AddDays(-1),
                date == today, rows);
            cache.Set(key, result, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
            return result;
        }
        finally { gate.Release(); }
    }
}
