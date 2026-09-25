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
    IReadOnlyList<string> Notes)
{
    public IReadOnlyList<ConveyorEfficiencyDay> Days { get; init; } = [];
}

sealed record ConveyorEfficiencyDay(
    DateOnly Date,
    long RevenueRiskParcels,
    long AssessedOutcomes,
    long SuccessfulOutcomes,
    double EfficiencyPercent,
    DateTime? LastScan,
    bool IsPartial);

sealed record ConveyorEfficiencyCalculation(
    ConveyorEfficiencyMonth Month,
    IReadOnlyList<ConveyorEfficiencyDay> Days);

sealed class ConveyorEfficiencyService
{
    private const int CurrentCalculationVersion = 6;
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
            || cached.Days.Count == 0
            || cached.GeneratedAt < now.AddHours(-20);
    }

    public async Task RefreshCurrentMonthAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var now = EdiForecastArchive.LocalNow;
            var currentMonth = new DateOnly(now.Year, now.Month, 1);
            var fullRebuild = cached is null || cached.CalculationVersion != CurrentCalculationVersion;
            var refreshedCalculations = new List<ConveyorEfficiencyCalculation>();
            if (fullRebuild)
            {
                for (var offset = -11; offset <= 0; offset++)
                {
                    var month = currentMonth.AddMonths(offset);
                    refreshedCalculations.Add(await CalculateMonthAsync(month, month.AddMonths(1), month == currentMonth, cancellationToken));
                }
            }
            else
            {
                refreshedCalculations.Add(await CalculateMonthAsync(currentMonth, currentMonth.AddMonths(1), true, cancellationToken));
                if (now.Day <= 8 || !(cached?.Days.Any(day => day.Date < currentMonth) ?? false))
                {
                    var previousMonth = currentMonth.AddMonths(-1);
                    refreshedCalculations.Add(await CalculateMonthAsync(previousMonth, currentMonth, false, cancellationToken));
                }
            }
            var refreshedMonths = refreshedCalculations.Select(result => result.Month).ToArray();
            var refreshedDates = refreshedMonths.Select(month => month.Month).ToHashSet();
            var months = (cached?.Months ?? [])
                .Where(month => !refreshedDates.Contains(month.Month))
                .Concat(refreshedMonths)
                .OrderBy(month => month.Month)
                .TakeLast(12)
                .ToArray();
            var refreshedDayMonths = refreshedDates;
            var firstDailyDate = DateOnly.FromDateTime(now).AddDays(-29);
            var lastDailyDate = DateOnly.FromDateTime(now);
            var days = (cached?.Days ?? [])
                .Where(day => !refreshedDayMonths.Contains(new DateOnly(day.Date.Year, day.Date.Month, 1)))
                .Concat(refreshedCalculations.SelectMany(result => result.Days))
                .Where(day => day.Date >= firstDailyDate && day.Date <= lastDailyDate)
                .OrderBy(day => day.Date)
                .ToArray();
            cached = new ConveyorEfficiencySnapshot(currentMonth, CurrentCalculationVersion, months, now,
            [
                "Les résultats évalués proviennent des postes automatisés; les passages manuels ne sont pas ajoutés au dénominateur.",
                "Un résultat est problématique s'il contient un non-lu caméra, une chute 16 ou 98, une recirculation, ou une mesure manquante requise pour la facturation.",
                "Les mesures obtenues au scan manuel complètent celles des convoyeurs; hors convoyeur du sol, le colis doit avoir un poids et ses trois dimensions au total.",
                "Le poids et chacune des trois dimensions peuvent provenir de passages automatisés différents et de dépôts différents pour le même colis pendant le mois analysé et les sept jours qui l'entourent.",
                "Dès qu'un colis passe sur le convoyeur du sol de Saint-Hubert, ses dimensions manquantes sont exclues pour tous ses passages ultérieurs, peu importe le dépôt; le poids demeure requis lorsque le colis est facturé au poids.",
                "Une mesure est requise lorsqu'une ligne regul_weight_chg correspond au compte client et à la zone LOC_NAT_ZONE_ID du code postal de destination.",
                "Un résultat cumulant plusieurs problèmes compte une seule fois. Gilmore ne produit pas de mesures et n'est pas pénalisé pour le poids ou les dimensions.",
            ]) { Days = days };
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

    private async Task<ConveyorEfficiencyCalculation> CalculateMonthAsync(DateOnly month, DateOnly nextMonth, bool partial, CancellationToken cancellationToken)
    {
        var partitionNames = string.Join(',', new[] { month.AddDays(-7).Year, nextMonth.AddDays(7).Year }
            .Distinct()
            .Select(year => $"p{year}"));
        var sql = $"""
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
            history_measurement AS (
                SELECT ph.PARCEL_ID parcel_id,ph.WEIGHT weight,ph.LENGTH l,ph.WIDTH w,ph.HEIGHT h,
                       (ph.DEPOT_ID=1 AND ph.SOURCE_TYPE=200 AND ph.SOURCE_ID=3) is_floor_pass
                FROM parcel_history PARTITION ({partitionNames}) ph
                JOIN (SELECT DISTINCT parcel_id FROM ranked WHERE parcel_id IS NOT NULL AND parcel_id<>0) scope
                  ON scope.parcel_id=ph.PARCEL_ID
                WHERE ph.EXCEPTION=903
                  AND ph.SOURCE_TYPE IN (200,201)
                  AND ph.PARCEL_ID IS NOT NULL
                  AND ph.PARCEL_ID<>0
                  AND COALESCE(ph.VOID,0)=0
                  AND ph.DATE_INSERT>=@scanStart-INTERVAL 1 DAY
                  AND ph.DATE_INSERT<@scanEnd+INTERVAL 1 DAY
                  AND ph.DATE_LIV>=@scanStart
                  AND ph.DATE_LIV<@scanEnd
            ),
            measurement_observation AS (
                SELECT parcel_id,weight,l,w,h,(conveyor_key='sth-floor') is_floor_pass FROM automated_scans
                WHERE parcel_id IS NOT NULL AND parcel_id<>0
                UNION ALL
                SELECT parcel_id,weight,l,w,h,is_floor_pass FROM history_measurement
            ),
            measurement_resolution AS (
                SELECT parcel_id,
                       MAX(weight>0) has_weight,
                       (MAX(l>0) AND MAX(w>0) AND MAX(h>0)) has_dimensions,
                       MAX(is_floor_pass) has_floor_pass
                FROM measurement_observation
                GROUP BY parcel_id
            ),
            parcel_rollup AS (
                SELECT conveyor_key,supports_measurements,operational_date,parcel_id,
                       MAX(chute IN (16,98) OR (chute IS NOT NULL AND chute<>98 AND same_chute_occurrence>1)) operational_issue
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
                       (COALESCE(pm.has_weight,0) AND
                        (COALESCE(pm.has_floor_pass,0) OR COALESCE(pm.has_dimensions,0))) measurement_resolved,
                       (pr.operational_issue OR
                         (pr.supports_measurements AND wc.BILLING_ACCOUNT IS NOT NULL AND
                          NOT (COALESCE(pm.has_weight,0) AND
                               (COALESCE(pm.has_floor_pass,0) OR COALESCE(pm.has_dimensions,0))))) is_problem
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
                SELECT operational_date,COUNT(*) readable_parcel_days,
                       COALESCE(SUM(operational_issue),0) operational_problem_parcels,
                       COALESCE(SUM(supports_measurements AND billed_by_weight),0) weight_billed_parcels,
                       COALESCE(SUM(supports_measurements AND NOT billed_by_weight),0) parcel_billed_parcels,
                       COALESCE(SUM(supports_measurements AND NOT billed_by_weight AND NOT measurement_resolved),0) excluded_measurement_issues,
                       COALESCE(SUM(supports_measurements AND billed_by_weight AND NOT measurement_resolved),0) revenue_risk_parcels,
                       COALESCE(SUM(supports_measurements AND zone_id IS NULL),0) unknown_zone_parcels,
                       COALESCE(SUM(is_problem),0) problem_readable_parcels,
                       COALESCE(SUM(NOT is_problem),0) successful_readable_parcels
                FROM classified
                GROUP BY operational_date
            ),
            no_read_summary AS (
                SELECT operational_date,
                       COALESCE(SUM((parcel_id IS NULL OR parcel_id=0) AND COALESCE(camera_data,'') LIKE '?%'),0) no_read_passages,
                       MAX(date_insert) last_scan
                FROM ranked
                GROUP BY operational_date
            ),
            operational_dates AS (
                SELECT operational_date FROM readable_summary
                UNION
                SELECT operational_date FROM no_read_summary
            )
            SELECT d.operational_date,
                   COALESCE(rs.readable_parcel_days,0) readable_parcel_days,
                   COALESCE(nr.no_read_passages,0) no_read_passages,
                   COALESCE(rs.operational_problem_parcels,0) operational_problem_parcels,
                   COALESCE(rs.weight_billed_parcels,0) weight_billed_parcels,
                   COALESCE(rs.parcel_billed_parcels,0) parcel_billed_parcels,
                   COALESCE(rs.excluded_measurement_issues,0) excluded_measurement_issues,
                   COALESCE(rs.revenue_risk_parcels,0) revenue_risk_parcels,
                   COALESCE(rs.unknown_zone_parcels,0) unknown_zone_parcels,
                   COALESCE(rs.readable_parcel_days,0)+COALESCE(nr.no_read_passages,0) assessed_outcomes,
                   COALESCE(rs.problem_readable_parcels,0)+COALESCE(nr.no_read_passages,0) problem_outcomes,
                   COALESCE(rs.successful_readable_parcels,0) successful_outcomes,
                   ROUND(100*COALESCE(rs.successful_readable_parcels,0)/NULLIF(COALESCE(rs.readable_parcel_days,0)+COALESCE(nr.no_read_passages,0),0),3) efficiency_percent,
                   nr.last_scan
            FROM operational_dates d
            LEFT JOIN readable_summary rs ON rs.operational_date=d.operational_date
            LEFT JOIN no_read_summary nr ON nr.operational_date=d.operational_date
            ORDER BY d.operational_date
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
        var dailyRows = new List<ConveyorEfficiencyMonth>();
        while (await reader.ReadAsync(cancellationToken))
        {
            dailyRows.Add(new ConveyorEfficiencyMonth(
                DateOnly.FromDateTime(reader.GetDateTime("operational_date")),
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
                false));
        }

        var assessedOutcomes = dailyRows.Sum(day => day.AssessedOutcomes);
        var successfulOutcomes = dailyRows.Sum(day => day.SuccessfulOutcomes);
        var monthSummary = new ConveyorEfficiencyMonth(
            month,
            dailyRows.Sum(day => day.ReadableParcelDays),
            dailyRows.Sum(day => day.NoReadPassages),
            dailyRows.Sum(day => day.OperationalProblemParcels),
            dailyRows.Sum(day => day.WeightBilledParcels),
            dailyRows.Sum(day => day.ParcelBilledParcels),
            dailyRows.Sum(day => day.ExcludedMeasurementIssues),
            dailyRows.Sum(day => day.RevenueRiskParcels),
            dailyRows.Sum(day => day.UnknownZoneParcels),
            assessedOutcomes,
            dailyRows.Sum(day => day.ProblemOutcomes),
            successfulOutcomes,
            assessedOutcomes == 0 ? 0 : Math.Round(100d * successfulOutcomes / assessedOutcomes, 3),
            dailyRows.Select(day => day.LastScan).Max(),
            partial);
        var currentDate = DateOnly.FromDateTime(EdiForecastArchive.LocalNow);
        var days = dailyRows.Select(day => new ConveyorEfficiencyDay(
            day.Month,
            day.RevenueRiskParcels,
            day.AssessedOutcomes,
            day.SuccessfulOutcomes,
            day.EfficiencyPercent,
            day.LastScan,
            day.Month == currentDate)).ToArray();
        return new ConveyorEfficiencyCalculation(monthSummary, days);
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
