using Microsoft.Extensions.Caching.Memory;
using MySqlConnector;

sealed record EdiVolumeHistoryDay(DateOnly Date, long Parcels, bool Partial);
sealed record EdiHistoryResponse(DateOnly Date, DateTime AsOf, IReadOnlyList<EdiVolumeHistoryDay> Days);

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
