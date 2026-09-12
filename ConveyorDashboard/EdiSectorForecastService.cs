using MySqlConnector;
using System.Text.Json;

sealed class EdiSectorForecastService(DashboardConfig config, IWebHostEnvironment environment)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string directory = Path.Combine(Environment.GetEnvironmentVariable("EDI_FORECAST_PATH")
        ?? Path.Combine(environment.ContentRootPath, "App_Data", "edi-forecasts"), "sectors-st-hubert");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string postalCte = """
            postal_map AS (
                SELECT REPLACE(UPPER(TRIM(l.LOC_POSTAL_CODE)), ' ', '') AS postal_code,
                       COUNT(DISTINCT COALESCE(NULLIF(l.SECTOR_ID,0),NULLIF(lr.SECTOR_ID,0))) AS sector_count,
                       MIN(COALESCE(NULLIF(l.SECTOR_ID,0),NULLIF(lr.SECTOR_ID,0))) AS sector_id
                FROM location l LEFT JOIN route lr ON lr.ROUTE_ID=l.ROUTE_ID
                GROUP BY REPLACE(UPPER(TRIM(l.LOC_POSTAL_CODE)), ' ', '')
            )
            """;
    private EdiSectorForecastResponse? cached;
    private Dictionary<int, (string? Contact, int Routes)>? sectorDetails;
    private DateTimeOffset sectorDetailsExpires;

    public async Task<EdiSectorForecastResponse> WithCurrentSectorDetailsAsync(EdiSectorForecastResponse result, CancellationToken cancellationToken)
    {
        if (sectorDetails == null || DateTimeOffset.UtcNow >= sectorDetailsExpires)
        {
        await using var connection = new MySqlConnection(config.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("""
            WITH recent_routes AS (
                SELECT DISTINCT DEST_ROUTE_ID FROM shipment
                WHERE INSERT_DATE>=@start AND INSERT_DATE<@end
                  AND SHIPMENT_STATUS NOT IN (500,501) AND PARCEL_NB>0
            ), route_counts AS (
                SELECT r.SECTOR_ID,COUNT(DISTINCT r.ROUTE_ID) AS active_routes
                FROM route r JOIN recent_routes used ON used.DEST_ROUTE_ID=r.ROUTE_ID
                GROUP BY r.SECTOR_ID
            )
            SELECT si.SECTOR_ID,NULLIF(TRIM(si.SECTOR_CONTACT),'') AS contact,
                   COALESCE(rc.active_routes,0) AS active_routes
            FROM sector_info si LEFT JOIN route_counts rc ON rc.SECTOR_ID=si.SECTOR_ID
            WHERE si.DEPOTNUMBER=1
            """, connection) { CommandTimeout = 30 };
        var today = EdiForecastArchive.LocalNow.Date;
        command.Parameters.AddWithValue("@start", today.AddDays(-28));
        command.Parameters.AddWithValue("@end", today);
        var details = new Dictionary<int, (string? Contact, int Routes)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            details[reader.GetInt32("SECTOR_ID")] = (reader.IsDBNull(reader.GetOrdinal("contact")) ? null : reader.GetString("contact"), reader.GetInt32("active_routes"));
        sectorDetails = details;
        sectorDetailsExpires = DateTimeOffset.UtcNow.AddMinutes(5);
        }
        return result with { Sectors = result.Sectors.Select(s => s with {
            SectorContact = sectorDetails.GetValueOrDefault(s.SectorId).Contact,
            ActiveRoutes = sectorDetails.TryGetValue(s.SectorId, out var detail) ? detail.Routes : null
        }).ToArray() };
    }

    private static EdiSectorForecastResponse ApplySectorScope(EdiSectorForecastResponse result)
    {
        var excluded = result.Sectors.Where(s => s.SectorId is 500 or 501 or 539 or 641 or 643).ToArray();
        return result with
        {
            Sectors = result.Sectors.Where(s => s.SectorId is not (500 or 501 or 539 or 641 or 643)).ToArray(),
            OutsideDepotParcels = result.OutsideDepotParcels + excluded.Sum(s => s.HistoricalParcels)
        };
    }

    public async Task<EdiSectorForecastResponse> GetAsync(DateOnly date, bool persist, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = Path.Combine(directory, $"{date:yyyy-MM-dd}-sorted-v3.json");
            if (File.Exists(path)) return await WithCurrentSectorDetailsAsync(ApplySectorScope(JsonSerializer.Deserialize<EdiSectorForecastResponse>(await File.ReadAllTextAsync(path), Json)!), cancellationToken);
            if (cached?.AsOfDate == date && (!persist || !cached.Reconstructed)) return await WithCurrentSectorDetailsAsync(cached, cancellationToken);
            var result = ApplySectorScope(await CalculateAsync(date, !persist, cancellationToken));
            if (persist)
            {
                Directory.CreateDirectory(directory);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(result, Json));
                    File.Move(temporary, path, overwrite: false);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            cached = result;
            return await WithCurrentSectorDetailsAsync(result, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<EdiSectorForecastResponse> EmptyWeekAsync(DateOnly sunday, CancellationToken cancellationToken)
    {
        var definitions = await ReadDefinitionsAsync(cancellationToken);
        var sectors = definitions.Select(d => new EdiSectorPrediction(d.SectorId, d.Name, d.PostalCodes, 0, 0, 0, 0,
            EdiSectorModel.Build(sunday, d.SectorId, [], []))).ToArray();
        return await WithCurrentSectorDetailsAsync(ApplySectorScope(new(sunday, DateTimeOffset.UtcNow, true,
            "sorted-sector-delivery-v3", 1, "Saint-Hubert", sunday.AddDays(-EdiForecast.HistoryDays), 0, 0, 0, 0, 0, sectors)), cancellationToken);
    }

    private async Task<List<EdiSectorDefinition>> ReadDefinitionsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(config.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var definitions = new List<EdiSectorDefinition>();
        await using (var command = new MySqlCommand($"""
            WITH {postalCte}
            SELECT si.SECTOR_ID, COALESCE(si.SECTOR_NAME,'') AS sector_name, COUNT(pm.postal_code) AS postal_codes
            FROM sector_info si LEFT JOIN postal_map pm ON pm.sector_id=si.SECTOR_ID AND pm.sector_count=1
            WHERE si.DEPOTNUMBER=1
            GROUP BY si.SECTOR_ID,si.SECTOR_NAME ORDER BY si.SECTOR_ID
            """, connection) { CommandTimeout = 240 })
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) definitions.Add(new(reader.GetInt32("SECTOR_ID"), reader.GetString("sector_name"), reader.GetInt32("postal_codes")));
        }
        return definitions;
    }

    public async Task<EdiSectorHistorySet> ReadHistoryAsync(DateOnly startDate, DateOnly endDate, DateTime ingestedBefore, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(config.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = new List<EdiSectorHistory>();
        long outside = 0, unmapped = 0, ambiguous = 0, total = 0;
        var observedDates = new HashSet<DateOnly>();
        // Rank before destination joins: a parcel passing Friday AND Sunday belongs to one Monday.
        // Read complete delivery cohorts only. The 3-day lookback includes the first Monday's Friday.
        // Bound database work and temporary ranking tables to 35 delivery days per read.
        for (var chunkStart = startDate; chunkStart < endDate; chunkStart = chunkStart.AddDays(35))
        {
            var chunkEnd = chunkStart.AddDays(35) < endDate ? chunkStart.AddDays(35) : endDate;
            await using (var command = new MySqlCommand($"""
                WITH {postalCte}, shifts AS (
                  SELECT ph.PARCEL_ID,ph.SHIPPING_ID,ph.EXP_DATE,ph.DATE_LIV,ph.PARCEL_HISTORY_ID,
                         DATE(ph.DATE_LIV - INTERVAL 3 HOUR) AS shift_day
                  FROM parcel_history ph
                  WHERE ph.EXCEPTION=903 AND ph.DEPOT_ID=1
                    AND ((ph.SOURCE_TYPE=200 AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID IN (1,3))) OR ph.SOURCE_TYPE IN (201,202,204,205))
                    AND ph.PARCEL_ID IS NOT NULL AND ph.PARCEL_ID<>0 AND COALESCE(ph.VOID,0)=0
                    AND ph.DATE_INSERT>=@insertStart AND ph.DATE_INSERT<@insertEnd
                    AND ph.DATE_LIV>=@scanStart AND ph.DATE_LIV<@scanEnd
                    AND (HOUR(ph.DATE_LIV)>=15 OR HOUR(ph.DATE_LIV)<3)
                ), cohorts AS (
                  SELECT *, DATE_ADD(shift_day,INTERVAL (CASE WHEN WEEKDAY(shift_day)=4 THEN 3 ELSE 1 END) DAY) AS day
                  FROM shifts WHERE WEEKDAY(shift_day)<>5
                ), complete_days AS (
                  SELECT day,COUNT(DISTINCT shift_day)>=(CASE WHEN WEEKDAY(day)=0 THEN 2 ELSE 1 END) AS complete
                  FROM cohorts GROUP BY day
                ), ranked AS (
                  SELECT *,ROW_NUMBER() OVER (PARTITION BY day,PARCEL_ID ORDER BY DATE_LIV DESC,PARCEL_HISTORY_ID DESC) AS rn
                  FROM cohorts WHERE day>=@start AND day<@end
                ), classified AS (
                  SELECT q.day,q.PARCEL_ID,
                         CASE WHEN pm.sector_count>1 THEN -2
                              WHEN si.SECTOR_ID IS NULL THEN -1
                              WHEN si.DEPOTNUMBER=1 THEN si.SECTOR_ID ELSE 0 END AS sector_id,
                         CASE WHEN pm.sector_count=1 THEN 1 ELSE 0 END AS postal_match,
                         CASE WHEN pm.sector_count=1 AND r.SECTOR_ID>0 AND r.SECTOR_ID<>pm.sector_id THEN 1 ELSE 0 END AS route_conflict
                  FROM ranked q
                  LEFT JOIN shipment s ON s.SHIPPING_ID=q.SHIPPING_ID AND s.EXP_DATE=q.EXP_DATE
                  LEFT JOIN postal_map pm ON pm.postal_code=REPLACE(UPPER(TRIM(s.DEST_POSTAL_CODE)), ' ', '')
                  LEFT JOIN route r ON r.ROUTE_ID=s.DEST_ROUTE_ID
                  LEFT JOIN sector_info si ON si.SECTOR_ID=CASE WHEN pm.sector_count=1 THEN pm.sector_id
                      WHEN COALESCE(pm.sector_count,0)=0 THEN NULLIF(r.SECTOR_ID,0) END
                  WHERE q.rn=1
                ), unique_parcels AS (
                  SELECT day,PARCEL_ID,CASE WHEN COUNT(DISTINCT sector_id)>1 THEN -2 ELSE MIN(sector_id) END AS sector_id,
                         MAX(postal_match) AS postal_match,MAX(route_conflict) AS route_conflict
                  FROM classified GROUP BY day,PARCEL_ID
                )
                SELECT u.day,sector_id,MAX(c.complete) AS complete,COUNT(*) AS parcels,
                       SUM(postal_match) AS postal_parcels,
                       SUM(1-postal_match) AS route_parcels,
                       SUM(route_conflict) AS conflicts
                FROM unique_parcels u JOIN complete_days c ON c.day=u.day GROUP BY u.day,sector_id ORDER BY u.day,sector_id
                """, connection) { CommandTimeout = 300 })
            {
                var start = chunkStart;
                command.Parameters.AddWithValue("@start", start.ToDateTime(TimeOnly.MinValue));
                command.Parameters.AddWithValue("@end", chunkEnd.ToDateTime(TimeOnly.MinValue));
                command.Parameters.AddWithValue("@scanStart", start.AddDays(-3).ToDateTime(new TimeOnly(15, 0)));
                command.Parameters.AddWithValue("@scanEnd", chunkEnd.ToDateTime(new TimeOnly(3, 0)));
                command.Parameters.AddWithValue("@insertStart", start.AddDays(-4).ToDateTime(TimeOnly.MinValue));
                command.Parameters.AddWithValue("@insertEnd", ingestedBefore);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var day = DateOnly.FromDateTime(reader.GetDateTime("day"));
                    var sector = reader.GetInt32("sector_id");
                    var parcels = Convert.ToInt64(reader["parcels"]);
                    if (parcels < 0) throw new InvalidDataException("Volumes de tri négatifs : prévision sectorielle non enregistrée.");
                    if (Convert.ToInt32(reader["complete"]) == 1) observedDates.Add(day);
                    total += parcels;
                    if (sector == 0) outside += parcels;
                    else if (sector == -1) unmapped += parcels;
                    else if (sector == -2) ambiguous += parcels;
                    else rows.Add(new(day, sector, parcels, Convert.ToInt64(reader["postal_parcels"]),
                        Convert.ToInt64(reader["route_parcels"]), Convert.ToInt64(reader["conflicts"])));
                }
            }
        }
        return new(rows, observedDates.ToArray(), total, outside, unmapped, ambiguous);
    }

    private async Task<EdiSectorForecastResponse> CalculateAsync(DateOnly date, bool reconstructed, CancellationToken cancellationToken)
    {
        var definitions = await ReadDefinitionsAsync(cancellationToken);
        var source = await ReadHistoryAsync(date.AddDays(-EdiForecast.HistoryDays), date, date.ToDateTime(new TimeOnly(6, 0)), cancellationToken);
        var rows = source.Rows;
        var observedDates = source.ObservedDates;
        var (total, outside, unmapped, ambiguous) = (source.Total, source.Outside, source.Unmapped, source.Ambiguous);
        if (observedDates.Count == 0) throw new InvalidDataException("Aucune journée de tri disponible pour les secteurs.");
        var predictions = definitions.Select(sector =>
        {
            var history = rows.Where(r => r.SectorId == sector.SectorId).ToArray();
            return new EdiSectorPrediction(sector.SectorId, sector.Name, sector.PostalCodes, history.Sum(r => r.Parcels),
                history.Sum(r => r.PostalParcels), history.Sum(r => r.RouteFallbackParcels), history.Sum(r => r.ConflictingRouteParcels),
                EdiSectorModel.Build(date, sector.SectorId, history, observedDates.ToArray()));
        }).ToArray();
        if (predictions.Sum(s => s.HistoricalParcels) + outside + unmapped + ambiguous != total)
            throw new InvalidDataException("Les volumes sectoriels ne se réconcilient pas avec la source.");
        return new(date, DateTimeOffset.UtcNow, reconstructed, "sorted-sector-delivery-v3", 1, "Saint-Hubert",
            date.AddDays(-EdiForecast.HistoryDays), observedDates.Count, total, outside, unmapped, ambiguous, predictions);
    }
}

