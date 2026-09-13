using System.Text.Json;
using MySqlConnector;

sealed record ConveyorEfficiencyMonth(
    DateOnly Month,
    long ReadableParcelDays,
    long NoReadPassages,
    long OperationalProblemParcels,
    long WeightBilledParcels,
    long ParcelBilledParcels,
    long ExcludedMeasurementIssues,
    long RevenueRiskParcels,
    long UnknownZoneParcels,
    long AssessedOutcomes,
    long ProblemOutcomes,
    long SuccessfulOutcomes,
    double EfficiencyPercent,
    DateTime? LastScan,
    bool IsPartial);

sealed record ConveyorEfficiencySnapshot(
    DateOnly CurrentMonth,
    int CalculationVersion,
    IReadOnlyList<ConveyorEfficiencyMonth> Months,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<string> Notes);

sealed class ConveyorEfficiencyService
{
    private const int CurrentCalculationVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly DashboardConfig config;
    private readonly ILogger<ConveyorEfficiencyService> logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string archivePath;
    private readonly string seedPath;
    private ConveyorEfficiencySnapshot? cached;

    public ConveyorEfficiencyService(DashboardConfig config, IWebHostEnvironment environment, ILogger<ConveyorEfficiencyService> logger)
    {
        this.config = config;
        this.logger = logger;
        archivePath = Environment.GetEnvironmentVariable("CONVEYOR_EFFICIENCY_PATH")
            ?? Path.Combine(environment.ContentRootPath, "App_Data", "conveyor-efficiency.json");
        seedPath = Path.Combine(environment.ContentRootPath, "ConveyorEfficiencySeed.json");
        var archived = LoadSnapshot(archivePath);
        var seeded = LoadSnapshot(seedPath);
        cached = archived?.CalculationVersion == CurrentCalculationVersion ? archived : seeded;
    }

    public ConveyorEfficiencySnapshot? Current => cached;

    public bool NeedsRefresh(DateTimeOffset now)
    {
        var currentMonth = new DateOnly(now.Year, now.Month, 1);
        return cached is null
            || cached.CalculationVersion != CurrentCalculationVersion
            || cached.CurrentMonth != currentMonth
            || cached.GeneratedAt < now.AddHours(-20);
    }

    public async Task RefreshCurrentMonthAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var now = EdiForecastArchive.LocalNow;
            var currentMonth = new DateOnly(now.Year, now.Month, 1);
            var current = await CalculateMonthAsync(currentMonth, currentMonth.AddMonths(1), true, cancellationToken);
            var refreshedMonths = new List<ConveyorEfficiencyMonth> { current };
            if (now.Day <= 8)
            {
                var previousMonth = currentMonth.AddMonths(-1);
                refreshedMonths.Add(await CalculateMonthAsync(previousMonth, currentMonth, false, cancellationToken));
            }
            var refreshedDates = refreshedMonths.Select(month => month.Month).ToHashSet();
            var months = (cached?.Months ?? [])
                .Where(month => !refreshedDates.Contains(month.Month))
                .Concat(refreshedMonths)
                .OrderBy(month => month.Month)
                .TakeLast(12)
                .ToArray();
            cached = new ConveyorEfficiencySnapshot(currentMonth, CurrentCalculationVersion, months, now,
            [
                "Postes automatisés seulement; les LINE_ID 201xx des scans manuels sont exclus.",
                "Un résultat est problématique s'il contient un non-lu caméra, une chute 16 ou 98, une recirculation, ou une mesure manquante requise pour la facturation.",
                "Une mesure complète obtenue lors d'un autre passage automatisé dans les sept jours avant ou après le passage régularise le colis.",
                "Une mesure est requise lorsqu'une ligne regul_weight_chg correspond au compte client et à la zone LOC_NAT_ZONE_ID du code postal de destination.",
                "Un résultat cumulant plusieurs problèmes compte une seule fois. Gilmore ne produit pas de mesures et n'est pas pénalisé pour le poids ou les dimensions.",
            ]);
            await SaveSnapshotAsync(cached, cancellationToken);
        }
        finally { gate.Release(); }
    }

    private ConveyorEfficiencySnapshot? LoadSnapshot(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ConveyorEfficiencySnapshot>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Archive d'efficacité convoyeur illisible : {Path}", path);
            return null;
        }
    }

    private async Task SaveSnapshotAsync(ConveyorEfficiencySnapshot snapshot, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(archivePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = archivePath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken);
        File.Move(temporaryPath, archivePath, true);
    }

    private async Task<ConveyorEfficiencyMonth> CalculateMonthAsync(DateOnly month, DateOnly nextMonth, bool partial, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH automated_scans AS (
                SELECT psh.id, psh.depot_id, psh.line_id, psh.parcel_id, psh.chute, psh.camera_data,
                       psh.weight, psh.l, psh.w, psh.h, psh.date_insert,
                       CASE
                         WHEN psh.depot_id=1 AND psh.line_id IN (0,1) THEN 'sth-top'
                         WHEN psh.depot_id=1 AND psh.line_id=3 THEN 'sth-floor'
                         WHEN psh.depot_id=2 THEN 'quebec'
                         WHEN psh.depot_id=12 THEN 'toronto'
                         WHEN psh.depot_id=28 THEN 'gilmore'
                       END conveyor_key,
                       psh.depot_id<>28 supports_measurements,
                       CASE
                         WHEN psh.depot_id=2 AND TIME(psh.date_insert)>='13:00:00' THEN DATE(psh.date_insert)
                         WHEN psh.depot_id=2 THEN DATE(psh.date_insert-INTERVAL 1 DAY)
                         WHEN psh.depot_id=1 AND TIME(psh.date_insert)>='16:00:00' THEN DATE(psh.date_insert)
                         WHEN psh.depot_id=1 THEN DATE(psh.date_insert-INTERVAL 1 DAY)
                         WHEN TIME(psh.date_insert)>='15:00:00' THEN DATE(psh.date_insert)
                         ELSE DATE(psh.date_insert-INTERVAL 1 DAY)
                       END operational_date
                FROM parcel_scan_history psh
                WHERE psh.date_insert>=@scanStart AND psh.date_insert<@scanEnd
                  AND (
                       (psh.depot_id=1 AND psh.line_id IN (0,1,3) AND (TIME(psh.date_insert)>='16:00:00' OR TIME(psh.date_insert)<'04:00:00'))
                    OR (psh.depot_id=2 AND psh.line_id=0 AND (TIME(psh.date_insert)>='13:00:00' OR TIME(psh.date_insert)<'07:00:00'))
                    OR (psh.depot_id IN (12,28) AND psh.line_id=0 AND (TIME(psh.date_insert)>='15:00:00' OR TIME(psh.date_insert)<'09:00:00'))
                  )
            ),
            ranked AS (
                SELECT s.*,
                       ROW_NUMBER() OVER (
                           PARTITION BY operational_date,depot_id,line_id,parcel_id,chute
                           ORDER BY date_insert,id
                       ) same_chute_occurrence
                FROM automated_scans s
                WHERE operational_date>=@monthStart AND operational_date<@monthEnd
            ),
            measurement_resolution AS (
                SELECT parcel_id,
                       MAX(weight>0 AND l>0 AND w>0 AND h>0) has_complete_measurement
                FROM automated_scans
                WHERE parcel_id IS NOT NULL AND parcel_id<>0
                GROUP BY parcel_id
            ),
            parcel_rollup AS (
                SELECT conveyor_key,supports_measurements,operational_date,parcel_id,
                       MAX(chute IN (16,98) OR (chute IS NOT NULL AND chute<>98 AND same_chute_occurrence>1)) operational_issue,
                       MAX(weight IS NULL OR weight<=0 OR l IS NULL OR l<=0 OR w IS NULL OR w<=0 OR h IS NULL OR h<=0) measurement_issue
                FROM ranked
                WHERE parcel_id IS NOT NULL AND parcel_id<>0
                GROUP BY conveyor_key,supports_measurements,operational_date,parcel_id
            ),
            parcel_ref AS (
                SELECT p.PARCEL_ID,MAX(NULLIF(p.CUSTOMER_ID,0)) customer_id,
                       MAX(p.SHIPPING_ID) shipping_id,MAX(p.EXP_DATE) exp_date
                FROM parcel p
                JOIN (SELECT DISTINCT parcel_id FROM parcel_rollup) scope ON scope.parcel_id=p.PARCEL_ID
                GROUP BY p.PARCEL_ID
            ),
            postal_zone AS (
                SELECT REPLACE(UPPER(TRIM(LOC_POSTAL_CODE)),' ','') postal_code,
                       MAX(NULLIF(TRIM(LOC_NAT_ZONE_ID),'')) zone_id
                FROM location
                GROUP BY REPLACE(UPPER(TRIM(LOC_POSTAL_CODE)),' ','')
                HAVING COUNT(DISTINCT NULLIF(TRIM(LOC_NAT_ZONE_ID),''))=1
            ),
            weight_charges AS (
                SELECT DISTINCT BILLING_ACCOUNT,TRIM(ZONE_ID) zone_id FROM regul_weight_chg
            ),
            classified AS (
                SELECT pr.*,
                       pz.zone_id,
                       wc.BILLING_ACCOUNT IS NOT NULL billed_by_weight,
                       COALESCE(pm.has_complete_measurement,0) has_complete_measurement,
                       (pr.operational_issue OR
                         (pr.supports_measurements AND wc.BILLING_ACCOUNT IS NOT NULL AND pr.measurement_issue
                          AND NOT COALESCE(pm.has_complete_measurement,0))) is_problem
                FROM parcel_rollup pr
                LEFT JOIN measurement_resolution pm ON pm.parcel_id=pr.parcel_id
                LEFT JOIN parcel_ref pref ON pref.PARCEL_ID=pr.parcel_id
                LEFT JOIN shipment s ON s.SHIPPING_ID=pref.shipping_id AND s.EXP_DATE=pref.exp_date
                LEFT JOIN customer c ON c.CUSTOMER_ID=COALESCE(NULLIF(s.CUSTOMER_ID,0),pref.customer_id)
                LEFT JOIN postal_zone pz ON pz.postal_code=REPLACE(UPPER(TRIM(s.DEST_POSTAL_CODE)),' ','')
                LEFT JOIN weight_charges wc
                  ON wc.BILLING_ACCOUNT=COALESCE(NULLIF(c.LINKED_ACCOUNT,0),c.CUSTOMER_ID)
                 AND wc.zone_id=pz.zone_id
            ),
            readable_summary AS (
                SELECT COUNT(*) readable_parcel_days,
                       COALESCE(SUM(operational_issue),0) operational_problem_parcels,
                       COALESCE(SUM(supports_measurements AND billed_by_weight),0) weight_billed_parcels,
                       COALESCE(SUM(supports_measurements AND NOT billed_by_weight),0) parcel_billed_parcels,
                       COALESCE(SUM(supports_measurements AND NOT billed_by_weight AND measurement_issue AND NOT has_complete_measurement),0) excluded_measurement_issues,
                       COALESCE(SUM(supports_measurements AND billed_by_weight AND measurement_issue AND NOT has_complete_measurement),0) revenue_risk_parcels,
                       COALESCE(SUM(supports_measurements AND zone_id IS NULL),0) unknown_zone_parcels,
                       COALESCE(SUM(is_problem),0) problem_readable_parcels,
                       COALESCE(SUM(NOT is_problem),0) successful_readable_parcels
                FROM classified
            ),
            no_read_summary AS (
                SELECT COALESCE(SUM((parcel_id IS NULL OR parcel_id=0) AND COALESCE(camera_data,'') LIKE '?%'),0) no_read_passages,
                       MAX(date_insert) last_scan
                FROM ranked
            )
            SELECT rs.readable_parcel_days,nr.no_read_passages,rs.operational_problem_parcels,
                   rs.weight_billed_parcels,rs.parcel_billed_parcels,rs.excluded_measurement_issues,
                   rs.revenue_risk_parcels,rs.unknown_zone_parcels,
                   rs.readable_parcel_days+nr.no_read_passages assessed_outcomes,
                   rs.problem_readable_parcels+nr.no_read_passages problem_outcomes,
                   rs.successful_readable_parcels successful_outcomes,
                   ROUND(100*rs.successful_readable_parcels/NULLIF(rs.readable_parcel_days+nr.no_read_passages,0),3) efficiency_percent,
                   nr.last_scan
            FROM readable_summary rs CROSS JOIN no_read_summary nr
            """;

        await using var connection = new MySqlConnection(config.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 300 };
        var start = month.ToDateTime(TimeOnly.MinValue);
        var end = nextMonth.ToDateTime(TimeOnly.MinValue);
        command.Parameters.AddWithValue("@monthStart", start);
        command.Parameters.AddWithValue("@monthEnd", end);
        command.Parameters.AddWithValue("@scanStart", start.AddDays(-7).AddHours(13));
        command.Parameters.AddWithValue("@scanEnd", end.AddDays(7).AddHours(9));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new ConveyorEfficiencyMonth(
            month,
            reader.GetInt64("readable_parcel_days"),
            reader.GetInt64("no_read_passages"),
            reader.GetInt64("operational_problem_parcels"),
            reader.GetInt64("weight_billed_parcels"),
            reader.GetInt64("parcel_billed_parcels"),
            reader.GetInt64("excluded_measurement_issues"),
            reader.GetInt64("revenue_risk_parcels"),
            reader.GetInt64("unknown_zone_parcels"),
            reader.GetInt64("assessed_outcomes"),
            reader.GetInt64("problem_outcomes"),
            reader.GetInt64("successful_outcomes"),
            reader.IsDBNull(reader.GetOrdinal("efficiency_percent")) ? 0 : reader.GetDouble("efficiency_percent"),
            reader.IsDBNull(reader.GetOrdinal("last_scan")) ? null : reader.GetDateTime("last_scan"),
            partial);
    }
}

sealed class ConveyorEfficiencyRefreshWorker(
    ConveyorEfficiencyService efficiency,
    ILogger<ConveyorEfficiencyRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = EdiForecastArchive.LocalNow;
            try
            {
                if (efficiency.NeedsRefresh(now)) await efficiency.RefreshCurrentMonthAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Actualisation de l'efficacité mensuelle des convoyeurs impossible; dernière archive conservée."); }

            var next = new DateTime(now.Year, now.Month, now.Day, 11, 0, 0, DateTimeKind.Local);
            if (next <= now) next = next.AddDays(1);
            try { await Task.Delay(next - now, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
