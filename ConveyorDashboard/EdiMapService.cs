using Microsoft.Extensions.Caching.Memory;
using MySqlConnector;

sealed record EdiMapPoint(double Latitude, double Longitude, long Parcels, long PostalParcels);
sealed record EdiMapResponse(DateOnly Date, DateTime AsOf, long Total, long Mapped, long Unmapped,
    long Ambiguous, long PostalParcels, IReadOnlyList<EdiMapPoint> Points);

sealed class EdiMapService(DashboardConfig config, ConveyorDataService edi)
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 8 });
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<EdiMapResponse> GetAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var summary = await edi.GetEdiDashboardAsync(date);
        var key = (date, summary.Nowcast.AsOf);
        if (cache.TryGetValue<EdiMapResponse>(key, out var ready)) return ready!;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<EdiMapResponse>(key, out var hit)) return hit!;
            await using var connection = new MySqlConnection(config.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand("""
                WITH postal_geo AS (
                  SELECT REPLACE(UPPER(TRIM(LOC_POSTAL_CODE)),' ','') postal_code,
                    COUNT(DISTINCT CONCAT(ROUND(LOC_LATITUDE,6),',',ROUND(LOC_LONGITUDE,6))) positions,
                    MIN(ROUND(LOC_LATITUDE,6)) latitude,MIN(ROUND(LOC_LONGITUDE,6)) longitude
                  FROM location
                  WHERE LOC_LATITUDE BETWEEN -90 AND 90 AND LOC_LONGITUDE BETWEEN -180 AND 180
                    AND NOT (LOC_LATITUDE=0 AND LOC_LONGITUDE=0)
                  GROUP BY REPLACE(UPPER(TRIM(LOC_POSTAL_CODE)),' ','')
                ), parcels AS (
                  SELECT SHIPPING_ID,EXP_DATE,COUNT(*) parcels FROM parcel
                  WHERE INSERT_DATE>=@start AND INSERT_DATE<@end AND PARCEL_STATUS NOT IN (500,501)
                  GROUP BY SHIPPING_ID,EXP_DATE
                ), candidates AS (
                  SELECT p.*,
                    CASE WHEN s.DEST_LAT BETWEEN -90 AND 90 AND s.DEST_LON BETWEEN -180 AND 180
                      AND NOT (s.DEST_LAT=0 AND s.DEST_LON=0) THEN ROUND(s.DEST_LAT,6)
                      WHEN pg.positions=1 THEN pg.latitude END latitude,
                    CASE WHEN s.DEST_LAT BETWEEN -90 AND 90 AND s.DEST_LON BETWEEN -180 AND 180
                      AND NOT (s.DEST_LAT=0 AND s.DEST_LON=0) THEN ROUND(s.DEST_LON,6)
                      WHEN pg.positions=1 THEN pg.longitude END longitude,
                    CASE WHEN s.DEST_LAT BETWEEN -90 AND 90 AND s.DEST_LON BETWEEN -180 AND 180
                      AND NOT (s.DEST_LAT=0 AND s.DEST_LON=0) THEN 0 ELSE 1 END postal,
                    COALESCE(pg.positions,0)>1 postal_ambiguous
                  FROM parcels p LEFT JOIN shipment s ON s.SHIPPING_ID=p.SHIPPING_ID AND s.EXP_DATE=p.EXP_DATE
                  LEFT JOIN postal_geo pg ON pg.postal_code=REPLACE(UPPER(TRIM(s.DEST_POSTAL_CODE)),' ','')
                ), assigned AS (
                  SELECT SHIPPING_ID,EXP_DATE,MAX(parcels) parcels,
                    COUNT(DISTINCT CONCAT(latitude,',',longitude)) positions,
                    MIN(latitude) latitude,MIN(longitude) longitude,
                    MAX(CASE WHEN latitude IS NOT NULL THEN postal ELSE 0 END) postal,
                    MAX(postal_ambiguous) postal_ambiguous
                  FROM candidates GROUP BY SHIPPING_ID,EXP_DATE
                ), located AS (
                  SELECT CASE WHEN positions=1 THEN latitude END latitude,
                    CASE WHEN positions=1 THEN longitude END longitude,parcels,
                    CASE WHEN positions=1 AND postal=1 THEN parcels ELSE 0 END postal_parcels,
                    CASE WHEN positions>1 OR (positions=0 AND postal_ambiguous=1) THEN parcels ELSE 0 END ambiguous
                  FROM assigned
                )
                SELECT latitude,longitude,SUM(parcels) parcels,SUM(postal_parcels) postal_parcels,SUM(ambiguous) ambiguous
                FROM located GROUP BY latitude,longitude ORDER BY parcels DESC
                """, connection) { CommandTimeout = 60 };
            command.Parameters.AddWithValue("@start", date.ToDateTime(new TimeOnly(4, 0)));
            command.Parameters.AddWithValue("@end", summary.Nowcast.AsOf);
            var points = new List<EdiMapPoint>();
            long total = 0, mapped = 0, ambiguous = 0, postal = 0;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var count = reader.GetInt64("parcels");
                total += count;
                ambiguous += reader.GetInt64("ambiguous");
                if (reader.IsDBNull(reader.GetOrdinal("latitude"))) continue;
                var approximate = reader.GetInt64("postal_parcels");
                mapped += count;
                postal += approximate;
                points.Add(new(reader.GetDouble("latitude"), reader.GetDouble("longitude"), count, approximate));
            }
            var result = new EdiMapResponse(date, summary.Nowcast.AsOf, total, mapped, total - mapped, ambiguous, postal, points);
            cache.Set(key, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60), Size = 1 });
            return result;
        }
        finally { gate.Release(); }
    }
}
