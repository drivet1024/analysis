using Microsoft.Extensions.Caching.Memory;
using MySqlConnector;

sealed record EdiDepotRow(int DepotId, string DepotName, long ParcelsToday, long ParcelsD7);
sealed record EdiDepotResponse(DateOnly Date, DateTime AsOf, IReadOnlyList<EdiDepotRow> Depots);

sealed record EdiDepotClientRow(int DepotId, string DepotName, long CustomerId, string CustomerName, long ParcelsToday, long ParcelsD7);
sealed record EdiDepotDataset(EdiDepotResponse Summary, IReadOnlyList<EdiDepotClientRow> Clients);
sealed record EdiDepotClientsResponse(DateOnly Date, DateTime AsOf, int DepotId, string DepotName, long Total, IReadOnlyList<EdiDepotClientRow> Clients);

sealed class EdiDepotService(DashboardConfig config, ConveyorDataService edi)
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 16 });
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<EdiDepotResponse> GetAsync(DateOnly date, CancellationToken cancellationToken)
        => (await ReadAsync(date, cancellationToken)).Summary;

    public async Task<EdiDepotClientsResponse?> GetClientsAsync(DateOnly date, int depotId, CancellationToken cancellationToken)
    {
        var data = await ReadAsync(date, cancellationToken);
        var depot = data.Summary.Depots.FirstOrDefault(row => row.DepotId == depotId);
        if (depot == null) return null;
        var clients = data.Clients.Where(row => row.DepotId == depotId && row.ParcelsToday > 0)
            .OrderByDescending(row => row.ParcelsToday).ThenBy(row => row.CustomerName).ThenBy(row => row.CustomerId).ToArray();
        return new(date, data.Summary.AsOf, depotId, depot.DepotName, depot.ParcelsToday, clients);
    }

    private async Task<EdiDepotDataset> ReadAsync(DateOnly date, CancellationToken cancellationToken)
    {
        // Reuse the exact cutoff displayed by the main EDI counters.
        var summary = await edi.GetEdiDashboardAsync(date);
        var key = (date, summary.Nowcast.AsOf);
        if (cache.TryGetValue<EdiDepotDataset>(key, out var ready)) return ready!;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<EdiDepotDataset>(key, out var hit)) return hit!;
            await using var connection = new MySqlConnection(config.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand("""
                WITH postal_map AS (
                 SELECT REPLACE(UPPER(TRIM(LOC_POSTAL_CODE)), ' ', '') postal_code,
                 COUNT(DISTINCT NULLIF(DEPOTNUMBER,0)) matches, MIN(NULLIF(DEPOTNUMBER,0)) depot_id
                 FROM location GROUP BY REPLACE(UPPER(TRIM(LOC_POSTAL_CODE)), ' ', '')
                ), parcels AS (
                 SELECT SHIPPING_ID,EXP_DATE,COALESCE(CUSTOMER_ID,0) CUSTOMER_ID,0 period,COUNT(*) parcels FROM parcel
                 WHERE INSERT_DATE>=@start AND INSERT_DATE<@end AND PARCEL_STATUS NOT IN (500,501)
                 GROUP BY SHIPPING_ID,EXP_DATE,CUSTOMER_ID
                 UNION ALL
                 SELECT SHIPPING_ID,EXP_DATE,COALESCE(CUSTOMER_ID,0) CUSTOMER_ID,1 period,COUNT(*) parcels FROM parcel
                 WHERE INSERT_DATE>=DATE_SUB(@start, INTERVAL 7 DAY) AND INSERT_DATE<DATE_SUB(@end, INTERVAL 7 DAY) AND PARCEL_STATUS NOT IN (500,501)
                 GROUP BY SHIPPING_ID,EXP_DATE,CUSTOMER_ID
                ), candidates AS (
                 SELECT p.*, CASE WHEN pm.matches>1 THEN -2 ELSE COALESCE(pm.depot_id,NULLIF(r.END_DEPOT_ID,0),NULLIF(si.DEPOTNUMBER,0),-1) END depot_id
                 FROM parcels p LEFT JOIN shipment s ON s.SHIPPING_ID=p.SHIPPING_ID AND s.EXP_DATE=p.EXP_DATE
                 LEFT JOIN postal_map pm ON pm.postal_code=REPLACE(UPPER(TRIM(s.DEST_POSTAL_CODE)), ' ', '')
                 LEFT JOIN route r ON r.ROUTE_ID=s.DEST_ROUTE_ID
                 LEFT JOIN sector_info si ON si.SECTOR_ID=s.DEST_SECTOR_ID
                ), assigned AS (
                 SELECT SHIPPING_ID,EXP_DATE,COALESCE(CUSTOMER_ID,0) CUSTOMER_ID,period,MAX(parcels) parcels,
                 CASE WHEN COUNT(DISTINCT depot_id)>1 THEN -2 ELSE MIN(depot_id) END depot_id
                 FROM candidates GROUP BY SHIPPING_ID,EXP_DATE,CUSTOMER_ID,period
                )
                SELECT a.depot_id,COALESCE(NULLIF(TRIM(d.DEPOTNAME),''),CASE WHEN a.depot_id=-2 THEN 'Destination ambiguë' WHEN a.depot_id=-1 THEN 'Destination non déterminée' ELSE CONCAT('Dépôt ',a.depot_id) END) depot_name,
                 a.CUSTOMER_ID,COALESCE(NULLIF(MAX(TRIM(c.NAME)),''),CASE WHEN a.CUSTOMER_ID=0 THEN 'Client non identifié' ELSE CONCAT('Client ',a.CUSTOMER_ID) END) customer_name,
                 SUM(CASE WHEN period=0 THEN parcels ELSE 0 END) parcels_today,
                 SUM(CASE WHEN period=1 THEN parcels ELSE 0 END) parcels_d7
                FROM assigned a LEFT JOIN depot d ON d.DEPOTNUMBER=a.depot_id
                LEFT JOIN customer c ON c.CUSTOMER_ID=a.CUSTOMER_ID
                GROUP BY a.depot_id,d.DEPOTNAME,a.CUSTOMER_ID ORDER BY parcels_today DESC,a.depot_id;
                """, connection) { CommandTimeout = 60 };
            command.Parameters.AddWithValue("@start", date.ToDateTime(new TimeOnly(4, 0)));
            command.Parameters.AddWithValue("@end", summary.Nowcast.AsOf);
            var rows = new List<EdiDepotClientRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetInt32("depot_id"), reader.GetString("depot_name"), reader.GetInt64("CUSTOMER_ID"), reader.GetString("customer_name"), reader.GetInt64("parcels_today"), reader.GetInt64("parcels_d7")));
            var depots = rows.GroupBy(row => row.DepotId).Select(group => new EdiDepotRow(group.Key,
                group.First().DepotName, group.Sum(row => row.ParcelsToday), group.Sum(row => row.ParcelsD7)))
                .OrderByDescending(row => row.ParcelsToday).ThenBy(row => row.DepotId).ToArray();
            var result = new EdiDepotDataset(new(date, summary.Nowcast.AsOf, depots), rows);
            cache.Set(key, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60), Size = 1 });
            return result;
        }
        finally { gate.Release(); }
    }
}
