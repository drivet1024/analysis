using MySqlConnector;

sealed record EdiChuteConfiguration(int Id, int ConveyorId, string Name, bool Active);
sealed record EdiDepotChuteRow(int ConveyorId, string ConveyorName, int? Chute, long Parcels, long Passages, long UnidentifiedPassages, IReadOnlyList<string> Destinations, IReadOnlyList<string> LocalFsas);
sealed record EdiDepotChutesResponse(DateOnly Date, DateTime AsOf, int DepotId, string DepotName,
    long Total, long TotalPassages, int? ConfigurationId, IReadOnlyList<EdiChuteConfiguration> Configurations, IReadOnlyList<EdiDepotChuteRow> Rows);

sealed class EdiDepotChuteService(DashboardConfig config, EdiDepotService depots)
{
    public async Task<EdiDepotChutesResponse?> GetAsync(DateOnly date, int depotId, int? configurationId, CancellationToken ct)
    {
        var summary = await depots.GetAsync(date, ct);
        var depot = summary.Depots.FirstOrDefault(d => d.DepotId == depotId);
        if (depot == null) return null;
        await using var connection = new MySqlConnection(config.ConnectionString);
        await connection.OpenAsync(ct);
        var configurations = new List<EdiChuteConfiguration>();
        await using (var command = new MySqlCommand("""
            SELECT cs.id,cs.conveyor_id,CONCAT(cl.CONVEYOR_NAME,' · ',cs.name) name,
                   EXISTS(SELECT 1 FROM conveyor c WHERE c.DEPOT_ID=@depot AND c.ENABLED=1 AND c.SHIFT_ID=cs.id) active_config
            FROM conveyor_shift cs JOIN conveyor_list cl ON cl.CONVEYOR_ID=cs.conveyor_id
            WHERE cl.DEPOT_ID=@depot AND EXISTS(SELECT 1 FROM conveyor_shift_route cr WHERE cr.shift_id=cs.id AND cr.conveyor_id=cs.conveyor_id)
            ORDER BY active_config DESC,cl.CONVEYOR_NAME,cs.name,cs.id
            """, connection) { CommandTimeout = 30 })
        {
            command.Parameters.AddWithValue("@depot", depotId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) configurations.Add(new(reader.GetInt32("id"), reader.GetInt32("conveyor_id"), reader.GetString("name"), reader.GetBoolean("active_config")));
        }
        if (configurationId.HasValue && !configurations.Any(c => c.Id == configurationId))
            throw new ArgumentException("Configuration de tri inconnue pour ce dépôt.");
        var destinations = new Dictionary<(int Conveyor, int Chute), HashSet<string>>();
        var fsas = new Dictionary<(int Conveyor, int Chute), HashSet<string>>();
        if (configurations.Count > 0)
        {
            await using var command = new MySqlCommand("""
                SELECT cr.route_id,cr.conveyor_id,cr.chute_no,r.END_DEPOT_ID,d.DEPOTNAME,
                       LEFT(l.LOC_POSTAL_CODE,3) fsa
                FROM conveyor_shift_route cr
                JOIN conveyor_shift cs ON cs.id=cr.shift_id AND cs.conveyor_id=cr.conveyor_id
                JOIN conveyor_list cl ON cl.CONVEYOR_ID=cs.conveyor_id AND cl.DEPOT_ID=@depot
                LEFT JOIN route r ON r.ROUTE_ID=cr.route_id
                LEFT JOIN depot d ON d.DEPOTNUMBER=r.END_DEPOT_ID
                LEFT JOIN location l ON l.ROUTE_ID=cr.route_id AND l.DEPOTNUMBER=@depot AND l.ENABLED=1
                WHERE cr.chute_no>0 AND (cr.shift_id=@configuration OR (@configuration IS NULL AND EXISTS(
                    SELECT 1 FROM conveyor c WHERE c.DEPOT_ID=@depot AND c.ENABLED=1 AND c.SHIFT_ID=cr.shift_id)))
                GROUP BY cr.route_id,cr.conveyor_id,cr.chute_no,r.END_DEPOT_ID,d.DEPOTNAME,LEFT(l.LOC_POSTAL_CODE,3)
                """, connection) { CommandTimeout = 30 };
            command.Parameters.AddWithValue("@depot", depotId);
            command.Parameters.AddWithValue("@configuration", (object?)configurationId ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var chute = (reader.GetInt32("conveyor_id"), reader.GetInt32("chute_no"));
                if (!destinations.TryGetValue(chute, out var names)) destinations[chute] = names = [];
                if (!fsas.TryGetValue(chute, out var local)) fsas[chute] = local = [];
                var endDepot = reader.IsDBNull(reader.GetOrdinal("END_DEPOT_ID")) ? 0 : reader.GetInt32("END_DEPOT_ID");
                if (endDepot > 0 && endDepot != depotId)
                    names.Add(reader.IsDBNull(reader.GetOrdinal("DEPOTNAME")) ? $"Dépôt {endDepot}" : reader.GetString("DEPOTNAME"));
                if (!reader.IsDBNull(reader.GetOrdinal("fsa")) && (endDepot == 0 || endDepot == depotId))
                {
                    var fsa = reader.GetString("fsa").Trim().ToUpperInvariant();
                    if (fsa.Length == 3) local.Add(fsa);
                    names.Add($"Local · {depot.DepotName}");
                }
                else if (endDepot == depotId) names.Add($"Local · {depot.DepotName}");
            }
        }
        var conveyorId = configurations.FirstOrDefault(c => c.Id == configurationId)?.ConveyorId;
        var counts = new Dictionary<(int Conveyor, int Chute), (long Parcels, long Passages, long Unknown)>();
        var otherConveyors = configurations.Select(c => c.ConveyorId).Distinct().ToArray();
        var otherConveyor = otherConveyors.Length == 1 ? otherConveyors[0] : 0;
        long total = 0, totalPassages = 0;
        await using (var command = new MySqlCommand("""
            WITH scans AS (
                SELECT chute,parcel_id,
                       CASE WHEN depot_id=1 AND line_id IN (0,1) THEN 1
                            WHEN depot_id=1 AND line_id=3 THEN 2 ELSE @otherConveyor END conveyor_id
                FROM parcel_scan_history
                WHERE depot_id=@depot AND date_insert>=@start AND date_insert<@end
                  AND (@depot<>1 OR @conveyor IS NULL
                       OR (@conveyor=1 AND line_id IN (0,1)) OR (@conveyor=2 AND line_id=3))
            ), totals AS (
                SELECT COUNT(DISTINCT NULLIF(parcel_id,0)) total,COUNT(*) total_passages FROM scans
            )
            SELECT chute,COUNT(DISTINCT NULLIF(parcel_id,0)) parcels,COUNT(*) passages,
                   SUM(parcel_id IS NULL OR parcel_id=0) unidentified_passages,
                   (SELECT total FROM totals) total,(SELECT total_passages FROM totals) total_passages,conveyor_id
            FROM scans GROUP BY conveyor_id,chute
            """, connection) { CommandTimeout = 30 })
        {
            command.Parameters.AddWithValue("@start", date.ToDateTime(new TimeOnly(4, 0)));
            command.Parameters.AddWithValue("@end", summary.AsOf);
            command.Parameters.AddWithValue("@depot", depotId);
            command.Parameters.AddWithValue("@otherConveyor", otherConveyor);
            command.Parameters.AddWithValue("@conveyor", (object?)conveyorId ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var chute = (reader.GetInt32("conveyor_id"), reader.IsDBNull(0) ? -1 : reader.GetInt32(0));
                counts[chute] = (reader.GetInt64("parcels"), reader.GetInt64("passages"), reader.GetInt64("unidentified_passages"));
                total = reader.GetInt64("total");
                totalPassages = reader.GetInt64("total_passages");
            }
        }
        var rows = counts.OrderBy(pair => pair.Key.Conveyor).ThenBy(pair => pair.Key.Chute < 0 ? int.MaxValue : pair.Key.Chute).Select(pair => new EdiDepotChuteRow(
            pair.Key.Conveyor, configurations.FirstOrDefault(c => c.ConveyorId == pair.Key.Conveyor)?.Name.Split(" · ")[0] ?? "Convoyeur non déterminé",
            pair.Key.Chute < 0 ? null : pair.Key.Chute, pair.Value.Parcels, pair.Value.Passages, pair.Value.Unknown,
            destinations.TryGetValue(pair.Key, out var names) ? names.Order().ToArray() : [],
            fsas.TryGetValue(pair.Key, out var local) ? local.Order().ToArray() : [])).ToArray();
        return new(date, summary.AsOf, depotId, depot.DepotName, total, totalPassages, configurationId, configurations, rows);
    }
}
