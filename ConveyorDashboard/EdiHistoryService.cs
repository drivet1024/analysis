using Microsoft.Extensions.Caching.Memory;
using MySqlConnector;

sealed record EdiVolumeHistoryDay(DateOnly Date, long Parcels, bool Partial);
sealed record EdiHistoryResponse(DateOnly Date, DateTime AsOf, IReadOnlyList<EdiVolumeHistoryDay> Days);
sealed record EdiFiscalWeek(int Week, DateOnly Start, DateOnly End, long Parcels,
    DateOnly PreviousStart, DateOnly PreviousEnd, long PreviousParcels, bool Partial, bool OpeningPartial);
sealed record EdiFiscalHistoryResponse(DateOnly Date, DateTime AsOf, DateOnly FiscalStart,
    DateOnly PreviousFiscalStart, IReadOnlyList<EdiFiscalWeek> Weeks);

sealed class EdiHistoryService(DashboardConfig config, ConveyorDataService edi)
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 8 });
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<EdiHistoryResponse> GetAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var summary = await edi.GetEdiDashboardAsync(date);
        var key = (date, summary.Nowcast.AsOf);
        if (cache.TryGetValue<EdiHistoryResponse>(key, out var hit)) return hit!;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<EdiHistoryResponse>(key, out hit)) return hit!;
            await using var connection = new MySqlConnection(config.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand("""
                SELECT DATE(INSERT_DATE - INTERVAL 4 HOUR) day, COUNT(*) parcels
                FROM parcel
                WHERE INSERT_DATE >= @start AND INSERT_DATE < @end AND PARCEL_STATUS NOT IN (500,501)
                GROUP BY DATE(INSERT_DATE - INTERVAL 4 HOUR)
                """, connection) { CommandTimeout = 60 };
            command.Parameters.AddWithValue("@start", date.AddDays(-29).ToDateTime(new TimeOnly(4, 0)));
            command.Parameters.AddWithValue("@end", summary.Nowcast.AsOf);
            var counts = new Dictionary<DateOnly, long>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                counts[DateOnly.FromDateTime(reader.GetDateTime("day"))] = reader.GetInt64("parcels");
            var days = Enumerable.Range(0, 30).Select(i => date.AddDays(i - 29))
                .Select(day => new EdiVolumeHistoryDay(day, counts.GetValueOrDefault(day),
                    day.AddDays(1).ToDateTime(new TimeOnly(4, 0)) > summary.Nowcast.AsOf)).ToArray();
            var result = new EdiHistoryResponse(date, summary.Nowcast.AsOf, days);
            cache.Set(key, result, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
            return result;
        }
        finally { gate.Release(); }
    }
}

sealed class EdiFiscalHistoryService(DashboardConfig config, ConveyorDataService edi)
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 8 });
    private readonly SemaphoreSlim gate = new(1, 1);

    private static DateOnly FiscalStart(DateOnly date) => new(date.Year - (date.Month < 6 ? 1 : 0), 6, 1);
    private static DateOnly SaturdayOnOrBefore(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 1) % 7));

    public async Task<EdiFiscalHistoryResponse> GetAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var summary = await edi.GetEdiDashboardAsync(date);
        var key = (date, summary.Nowcast.AsOf);
        if (cache.TryGetValue<EdiFiscalHistoryResponse>(key, out var hit)) return hit!;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<EdiFiscalHistoryResponse>(key, out hit)) return hit!;
            var fiscalStart = FiscalStart(date);
            var previousFiscalStart = fiscalStart.AddYears(-1);
            var firstWeek = SaturdayOnOrBefore(fiscalStart);
            var previousFirstWeek = SaturdayOnOrBefore(previousFiscalStart);
            var selectedWeek = SaturdayOnOrBefore(date);
            var weekCount = (selectedWeek.DayNumber - firstWeek.DayNumber) / 7 + 1;
            var selectedWeekStart = selectedWeek.ToDateTime(new TimeOnly(4, 0));
            var elapsed = summary.Nowcast.AsOf - selectedWeekStart;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            if (elapsed > TimeSpan.FromDays(7)) elapsed = TimeSpan.FromDays(7);
            var previousSelectedWeek = previousFirstWeek.AddDays((weekCount - 1) * 7);
            var previousAsOf = previousSelectedWeek.ToDateTime(new TimeOnly(4, 0)) + elapsed;

            await using var connection = new MySqlConnection(config.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand("""
                SELECT fiscal_year,week_start,SUM(parcels) parcels FROM (
                  SELECT 0 fiscal_year,
                    DATE_SUB(DATE(INSERT_DATE - INTERVAL 4 HOUR),
                      INTERVAL MOD(WEEKDAY(DATE(INSERT_DATE - INTERVAL 4 HOUR))+2,7) DAY) week_start,
                    COUNT(*) parcels
                  FROM parcel
                  WHERE INSERT_DATE>=@currentStart AND INSERT_DATE<@currentEnd
                    AND PARCEL_STATUS NOT IN (500,501)
                  GROUP BY week_start
                  UNION ALL
                  SELECT 1 fiscal_year,
                    DATE_SUB(DATE(INSERT_DATE - INTERVAL 4 HOUR),
                      INTERVAL MOD(WEEKDAY(DATE(INSERT_DATE - INTERVAL 4 HOUR))+2,7) DAY) week_start,
                    COUNT(*) parcels
                  FROM parcel
                  WHERE INSERT_DATE>=@previousStart AND INSERT_DATE<@previousEnd
                    AND PARCEL_STATUS NOT IN (500,501)
                  GROUP BY week_start
                ) weekly GROUP BY fiscal_year,week_start
                """, connection) { CommandTimeout = 120 };
            command.Parameters.AddWithValue("@currentStart", fiscalStart.ToDateTime(new TimeOnly(4, 0)));
            command.Parameters.AddWithValue("@currentEnd", summary.Nowcast.AsOf);
            command.Parameters.AddWithValue("@previousStart", previousFiscalStart.ToDateTime(new TimeOnly(4, 0)));
            command.Parameters.AddWithValue("@previousEnd", previousAsOf);
            var current = new Dictionary<DateOnly, long>();
            var previous = new Dictionary<DateOnly, long>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var target = reader.GetInt32("fiscal_year") == 0 ? current : previous;
                target[DateOnly.FromDateTime(reader.GetDateTime("week_start"))] = reader.GetInt64("parcels");
            }

            static DateOnly InclusiveEnd(DateTime endExclusive) => DateOnly.FromDateTime(endExclusive.AddHours(-4).AddTicks(-1));
            var weeks = Enumerable.Range(0, weekCount).Select(index =>
            {
                var weekStart = firstWeek.AddDays(index * 7);
                var priorStart = previousFirstWeek.AddDays(index * 7);
                var currentEnd = index == weekCount - 1 ? summary.Nowcast.AsOf : weekStart.AddDays(7).ToDateTime(new TimeOnly(4, 0));
                var priorEnd = index == weekCount - 1 ? previousAsOf : priorStart.AddDays(7).ToDateTime(new TimeOnly(4, 0));
                return new EdiFiscalWeek(index + 1,
                    weekStart < fiscalStart ? fiscalStart : weekStart, InclusiveEnd(currentEnd), current.GetValueOrDefault(weekStart),
                    priorStart < previousFiscalStart ? previousFiscalStart : priorStart, InclusiveEnd(priorEnd), previous.GetValueOrDefault(priorStart),
                    index == weekCount - 1 && elapsed < TimeSpan.FromDays(7),
                    weekStart < fiscalStart || priorStart < previousFiscalStart);
            }).ToArray();
            var result = new EdiFiscalHistoryResponse(date, summary.Nowcast.AsOf, fiscalStart, previousFiscalStart, weeks);
            cache.Set(key, result, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
            return result;
        }
        finally { gate.Release(); }
    }
}
