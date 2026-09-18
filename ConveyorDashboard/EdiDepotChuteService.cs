using MySqlConnector;

sealed record EdiDepotChuteRow(int? Chute, long? Sector, long Parcels, long Passages, IReadOnlyList<string> LocalFsas);
sealed record EdiDepotChutesResponse(DateOnly Date, DateTime AsOf, int DepotId, string DepotName,
    long Total, long TotalPassages, IReadOnlyList<EdiDepotChuteRow> Rows);

sealed class EdiDepotChuteService(DashboardConfig config, EdiDepotService depots)
{
    public async Task<EdiDepotChutesResponse?> GetAsync(DateOnly date, int depotId, CancellationToken ct)
    {
        var summary = await depots.GetAsync(date, ct);
        var depot = summary.Depots.FirstOrDefault(d => d.DepotId == depotId);
        if (depot == null) return null;
        await using var connection = new MySqlConnection(config.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("""
            WITH scans AS (
                SELECT chute,parcel_id
                FROM parcel_scan_history
                WHERE depot_id=1 AND line_id IN (0,1)
                  AND date_insert>=@start AND date_insert<@end
                  AND parcel_id IS NOT NULL AND parcel_id<>0
            ), scanned_parcels AS (
                SELECT DISTINCT p.PARCEL_ID,p.SHIPMENT_INTERNAL_ID,p.SHIPPING_ID,p.EXP_DATE
                FROM parcel p JOIN (SELECT DISTINCT parcel_id FROM scans) s ON s.parcel_id=p.PARCEL_ID
            ), shipments AS (
                SELECT p.PARCEL_ID,s.DEST_POSTAL_CODE,s.DEST_ROUTE_ID,s.DEST_SECTOR_ID
                FROM scanned_parcels p LEFT JOIN shipment s ON s.ID=p.SHIPMENT_INTERNAL_ID
                WHERE p.SHIPMENT_INTERNAL_ID>0
                UNION ALL
                SELECT p.PARCEL_ID,s.DEST_POSTAL_CODE,s.DEST_ROUTE_ID,s.DEST_SECTOR_ID
                FROM scanned_parcels p LEFT JOIN shipment s ON s.SHIPPING_ID=p.SHIPPING_ID AND s.EXP_DATE=p.EXP_DATE
                WHERE p.SHIPMENT_INTERNAL_ID IS NULL OR p.SHIPMENT_INTERNAL_ID=0
            ), candidates AS (
                SELECT s.PARCEL_ID,
                       COALESCE(NULLIF(l.DEPOTNUMBER,0),NULLIF(r.END_DEPOT_ID,0),NULLIF(si.DEPOTNUMBER,0),-1) destination,
                       COALESCE(NULLIF(s.DEST_SECTOR_ID,0),-1) sector,
                       LEFT(REPLACE(UPPER(TRIM(s.DEST_POSTAL_CODE)),' ',''),3) fsa
                FROM shipments s
                LEFT JOIN location l ON l.LOC_POSTAL_CODE=REPLACE(UPPER(TRIM(s.DEST_POSTAL_CODE)),' ','')
                LEFT JOIN route r ON r.ROUTE_ID=s.DEST_ROUTE_ID
                LEFT JOIN sector_info si ON si.SECTOR_ID=s.DEST_SECTOR_ID
            ), assigned AS (
                SELECT PARCEL_ID,MIN(destination) destination,
                       CASE WHEN MIN(sector)=MAX(sector) THEN NULLIF(MIN(sector),-1) END sector,
                       CASE WHEN COUNT(DISTINCT fsa)=1 THEN MIN(fsa) END fsa
                FROM candidates GROUP BY PARCEL_ID
                HAVING MIN(destination)=@depot AND MAX(destination)=@depot
            ), matched AS (
                SELECT s.chute,s.parcel_id,a.sector,a.fsa FROM scans s JOIN assigned a ON a.PARCEL_ID=s.parcel_id
            )
            SELECT chute,sector,COUNT(DISTINCT parcel_id) parcels,COUNT(*) passages,
                   GROUP_CONCAT(DISTINCT fsa) fsas,
                   (SELECT COUNT(DISTINCT parcel_id) FROM matched) total,
                   (SELECT COUNT(*) FROM matched) total_passages
            FROM matched GROUP BY chute,sector ORDER BY chute,sector
            """, connection) { CommandTimeout = 30 };
        command.Parameters.AddWithValue("@start", date.ToDateTime(new TimeOnly(4, 0)));
        command.Parameters.AddWithValue("@end", summary.AsOf);
        command.Parameters.AddWithValue("@depot", depotId);
        long total = 0, totalPassages = 0;
        var rows = new List<EdiDepotChuteRow>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new(reader.IsDBNull(0) ? null : reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),reader.GetInt64("parcels"),reader.GetInt64("passages"),
                reader.IsDBNull(reader.GetOrdinal("fsas")) ? [] : reader.GetString("fsas").Split(',', StringSplitOptions.RemoveEmptyEntries).Order().ToArray()));
            total = reader.GetInt64("total");
            totalPassages = reader.GetInt64("total_passages");
        }
        return new(date, summary.AsOf, depotId, depot.DepotName, total, totalPassages, rows);
    }
}
