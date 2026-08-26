using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);

LoadEnvironmentFile(Path.Combine(builder.Environment.ContentRootPath, ".env.local"));
LoadEnvironmentFile(Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "MySqlTool", ".env.local")));

var config = new DashboardConfig(
    Environment.GetEnvironmentVariable("MYSQL_HOST") ?? "192.168.1.222",
    uint.TryParse(Environment.GetEnvironmentVariable("MYSQL_PORT"), out var port) ? port : 3306,
    Environment.GetEnvironmentVariable("MYSQL_DATABASE") ?? "nationex",
    Environment.GetEnvironmentVariable("MYSQL_USER") ?? "user_ro",
    Environment.GetEnvironmentVariable("MYSQL_PASSWORD") ?? string.Empty,
    Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty,
    Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5.6-luna");

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<ConveyorDataService>();
builder.Services.AddHttpClient<OpenAiAnalysisService>(client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/");
    client.Timeout = TimeSpan.FromMinutes(5);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = !HasDiscardProxy() });

var app = builder.Build();
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate",
});

app.MapGet("/api/status", async (ConveyorDataService data, DashboardConfig settings) =>
{
    try
    {
        return Results.Ok(new
        {
            databaseConnected = await data.PingAsync(),
            databaseConfigured = !string.IsNullOrWhiteSpace(settings.MySqlPassword),
            openAiConfigured = !string.IsNullOrWhiteSpace(settings.OpenAiApiKey),
            model = settings.OpenAiModel,
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { databaseConnected = false, databaseConfigured = true, databaseError = ex.Message, openAiConfigured = !string.IsNullOrWhiteSpace(settings.OpenAiApiKey), model = settings.OpenAiModel });
    }
});

app.MapGet("/api/catalog", () => Results.Ok(ConveyorCatalog.All.Select(x => new
{
    x.Key,
    x.Name,
    x.Site,
    x.DepotId,
    x.StartHour,
    x.EndHour,
    x.SupportsMeasurements,
})));

app.MapGet("/api/range", async (ConveyorDataService data) =>
{
    try { return Results.Ok(await data.GetAvailableRangeAsync()); }
    catch (Exception ex) { return Results.Problem($"Impossible de lire les dates disponibles : {ex.Message}"); }
});

app.MapGet("/api/daily", async (HttpRequest request, ConveyorDataService data) =>
{
    try
    {
        var date = QueryDate(request.Query, "date", DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));
        var conveyor = request.Query["conveyor"].FirstOrDefault() ?? "all";
        return Results.Ok(await data.GetDailyAsync(date, conveyor));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { return Results.Problem($"L'analyse quotidienne n'a pas pu être calculée : {ex.Message}"); }
});

app.MapGet("/api/details", async (HttpRequest request, ConveyorDataService data) =>
{
    try
    {
        var date = QueryDate(request.Query, "date", DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));
        var conveyor = request.Query["conveyor"].FirstOrDefault() ?? "all";
        var metric = request.Query["metric"].FirstOrDefault() ?? "total";
        var page = int.TryParse(request.Query["page"], out var parsedPage) ? Math.Max(1, parsedPage) : 1;
        var pageSize = int.TryParse(request.Query["pageSize"], out var parsedSize) ? Math.Clamp(parsedSize, 10, 100) : 50;
        int? customerId = int.TryParse(request.Query["customerId"], out var parsedCustomer) ? parsedCustomer : null;
        return Results.Ok(await data.GetDetailsAsync(date, conveyor, metric, page, pageSize, customerId));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { return Results.Problem($"Le détail n'a pas pu être chargé : {ex.Message}"); }
});

app.MapGet("/api/customers", async (HttpRequest request, ConveyorDataService data) =>
{
    try
    {
        var date = QueryDate(request.Query, "date", DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));
        var conveyor = request.Query["conveyor"].FirstOrDefault() ?? "all";
        var minVolume = int.TryParse(request.Query["minVolume"], out var parsedVolume) ? Math.Clamp(parsedVolume, 1, 100_000) : 100;
        var sort = request.Query["sort"].FirstOrDefault() ?? "problemRate";
        var search = request.Query["search"].FirstOrDefault();
        return Results.Ok(await data.GetCustomersAsync(date, conveyor, minVolume, sort, search));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { return Results.Problem($"L'analyse client n'a pas pu être calculée : {ex.Message}"); }
});

app.MapGet("/api/exception25", async (HttpRequest request, ConveyorDataService data) =>
{
    try
    {
        var endDate = QueryDate(request.Query, "endDate", DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));
        var conveyor = request.Query["conveyor"].FirstOrDefault() ?? "all";
        return Results.Ok(await data.GetException25Async(endDate, conveyor));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { return Results.Problem($"L'analyse de l'exception 25 n'a pas pu être calculée : {ex.Message}"); }
});

app.MapPost("/api/ai-analysis", async (AiAnalysisRequest request, ConveyorDataService data, OpenAiAnalysisService openAi, DashboardConfig settings) =>
{
    if (string.IsNullOrWhiteSpace(settings.OpenAiApiKey))
        return Results.Problem("La clé API OpenAI n'est pas configurée.", statusCode: StatusCodes.Status503ServiceUnavailable);
    try
    {
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException("La date doit être au format AAAA-MM-JJ.");
        var daily = await data.GetDailyAsync(date, request.Conveyor);
        var customers = await data.GetCustomersAsync(date, request.Conveyor, Math.Clamp(request.MinVolume, 1, 100_000), "problemRate", null);
        return Results.Ok(new { analysis = await openAi.AnalyzeAsync(daily, customers), model = settings.OpenAiModel, generatedAt = DateTimeOffset.Now });
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (OpenAiException ex) { return Results.Problem(ex.Message, statusCode: ex.StatusCode); }
    catch (Exception ex) { return Results.Problem($"L'analyse OpenAI n'a pas pu être produite : {ex.Message}"); }
});

app.MapFallbackToFile("index.html");
app.Run();

static DateOnly QueryDate(IQueryCollection query, string name, DateOnly fallback)
{
    var value = query[name].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(value)) return fallback;
    if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)) return result;
    throw new ArgumentException($"Le paramètre {name} doit être au format AAAA-MM-JJ.");
}

static void LoadEnvironmentFile(string path)
{
    if (!File.Exists(path)) return;
    foreach (var rawLine in File.ReadAllLines(path))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#')) continue;
        var separator = line.IndexOf('=');
        if (separator <= 0) continue;
        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))) Environment.SetEnvironmentVariable(key, value);
    }
}

static bool HasDiscardProxy()
{
    foreach (var variable in new[] { "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY" })
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (Uri.TryCreate(value, UriKind.Absolute, out var proxy) && proxy.IsLoopback && proxy.Port == 9) return true;
    }
    return false;
}

sealed record DashboardConfig(string MySqlHost, uint MySqlPort, string MySqlDatabase, string MySqlUser, string MySqlPassword, string OpenAiApiKey, string OpenAiModel)
{
    public string ConnectionString => new MySqlConnectionStringBuilder
    {
        Server = MySqlHost,
        Port = MySqlPort,
        Database = MySqlDatabase,
        UserID = MySqlUser,
        Password = MySqlPassword,
        ConnectionTimeout = 8,
        DefaultCommandTimeout = 300,
        SslMode = MySqlSslMode.None,
        AllowPublicKeyRetrieval = true,
    }.ConnectionString;
}

sealed record ConveyorDefinition(string Key, string Name, string Site, int DepotId, int StartHour, int EndHour, string SourcePredicate, bool SupportsMeasurements);

static class ConveyorCatalog
{
    public static readonly IReadOnlyList<ConveyorDefinition> All =
    [
        new("sth-top", "St-Hubert — haut", "St-Hubert", 1, 15, 3, "(ph.SOURCE_ID IS NULL OR ph.SOURCE_ID = 1)", true),
        new("sth-floor", "St-Hubert — sol", "St-Hubert", 1, 15, 3, "ph.SOURCE_ID = 3", true),
        new("quebec", "Québec", "Québec", 2, 13, 7, "ph.SOURCE_ID IS NULL", true),
        new("toronto", "Toronto", "Toronto", 12, 15, 9, "ph.SOURCE_ID IS NULL", true),
        new("gilmore", "Gilmore", "Gilmore", 28, 15, 9, "ph.SOURCE_ID IS NULL", false),
    ];

    public static IReadOnlyList<ConveyorDefinition> Resolve(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Equals("all", StringComparison.OrdinalIgnoreCase)) return All;
        if (key.Equals("st-hubert", StringComparison.OrdinalIgnoreCase)) return All.Where(x => x.DepotId == 1).ToArray();
        var item = All.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return item is null ? throw new ArgumentException("Convoyeur inconnu.") : [item];
    }
}

sealed record AvailableRange(DateOnly? MinDate, DateOnly? MaxDate);
sealed record AiAnalysisRequest(string Date, string Conveyor, int MinVolume);
sealed record HourPoint(string ConveyorKey, int OperationalHour, DateTime BucketStart, long Passages, long UniqueParcels, long SameChuteRepeated)
{
    public double SameChuteRepeatedRate => UniqueParcels == 0 ? 0 : Math.Round((double)SameChuteRepeated / UniqueParcels, 6);
}
sealed record DailyMetric(
    string ConveyorKey,
    string ConveyorName,
    string Site,
    bool SupportsMeasurements,
    long UniqueParcels,
    long Passages,
    long Recirculated,
    long Chute98,
    long SameChuteRepeated,
    long? NoWeight,
    long? NoDimensions,
    long? Under1,
    long? From1To3,
    long? Over3To5,
    long? Over5To10,
    long? Over10)
{
    public double RecirculationRate => Rate(Recirculated, UniqueParcels);
    public double Chute98Rate => Rate(Chute98, UniqueParcels);
    public double SameChuteRepeatedRate => Rate(SameChuteRepeated, UniqueParcels);
    public double? NoWeightRate => NoWeight is null ? null : Rate(NoWeight.Value, UniqueParcels);
    public double? NoDimensionsRate => NoDimensions is null ? null : Rate(NoDimensions.Value, UniqueParcels);
    private static double Rate(long n, long d) => d == 0 ? 0 : Math.Round((double)n / d, 6);
}
sealed record DailyResponse(DateOnly OperationalDate, IReadOnlyList<DailyMetric> Metrics, IReadOnlyList<HourPoint> Hourly, IReadOnlyList<string> Notes, DateTimeOffset GeneratedAt);

sealed record ParcelDetail(long ParcelId, int? CustomerId, string CustomerName, string ConveyorKey, string ConveyorName, long Passages, DateTime FirstPassage, DateTime LastPassage, string PassageTimes, string Chutes, decimal? LatestValidWeight, decimal? LatestValidLength, decimal? LatestValidWidth, decimal? LatestValidHeight, bool Recirculated, bool Chute98, bool SameChuteRepeated, bool NoWeight, bool NoDimensions);
sealed record DetailResponse(DateOnly OperationalDate, string Metric, int Page, int PageSize, long TotalRows, IReadOnlyList<ParcelDetail> Rows);

sealed record CustomerRiskRow(int CustomerId, string CustomerName, long TotalParcelsGiven, long ConveyorParcels, long MeasurementParcels, long ProblemParcels, long Recirculated, long Chute98, long SameChuteRepeated, long? NoWeight, long? NoDimensions, long ValidWeightParcels, long Under1, long From1To3, long Over3To5, long Over5To10, long Over10, long ValidDimensionParcels, long VerySmallFormat, long AtypicalFormat)
{
    public double ConveyorCoverageRate => Rate(ConveyorParcels, TotalParcelsGiven);
    public double ProblemVsTotalRate => Rate(ProblemParcels, TotalParcelsGiven);
    public double ProblemOnConveyorRate => Rate(ProblemParcels, ConveyorParcels);
    public double RecirculationRate => Rate(Recirculated, ConveyorParcels);
    public double Chute98Rate => Rate(Chute98, ConveyorParcels);
    public double SameChuteRepeatedRate => Rate(SameChuteRepeated, ConveyorParcels);
    public double? NoWeightRate => NoWeight is null ? null : Rate(NoWeight.Value, MeasurementParcels);
    public double? NoDimensionsRate => NoDimensions is null ? null : Rate(NoDimensions.Value, MeasurementParcels);
    public double VeryLightRate => Rate(Under1, ValidWeightParcels);
    public double VerySmallFormatRate => Rate(VerySmallFormat, ValidDimensionParcels);
    public double AtypicalFormatRate => Rate(AtypicalFormat, ValidDimensionParcels);
    private static double Rate(long n, long d) => d == 0 ? 0 : Math.Round((double)n / d, 6);
}
sealed record CustomerResponse(DateOnly OperationalDate, string Conveyor, int MinimumVolume, long ConveyorParcelsWithCustomer, long ConveyorParcelsWithoutCustomer, IReadOnlyList<CustomerRiskRow> Customers, IReadOnlyList<string> Notes, DateTimeOffset GeneratedAt);

sealed record CorrelationSummary(long WithException25, long WithException25Problems, long WithoutException25, long WithoutException25Problems)
{
    public double RateWith => Rate(WithException25Problems, WithException25);
    public double RateWithout => Rate(WithoutException25Problems, WithoutException25);
    public double DifferencePoints => Math.Round((RateWith - RateWithout) * 100, 2);
    public double? RateRatio => RateWithout == 0 ? null : Math.Round(RateWith / RateWithout, 2);
    public string Classification => WithException25 < 100 || WithoutException25 < 100 ? "Données insuffisantes" : RateRatio >= 2 && DifferencePoints >= 10 ? "Lien fort" : RateRatio >= 1.5 && DifferencePoints >= 5 ? "Lien modéré" : "Aucun lien clair";
    private static double Rate(long n, long d) => d == 0 ? 0 : Math.Round((double)n / d, 6);
}
sealed record ExceptionCustomerRow(int CustomerId, string CustomerName, long ConveyorParcels, long ProblemParcels, long Exception25Parcels, long Exception25AndProblemParcels);
sealed record Exception25Response(DateOnly StartDate, DateOnly EndDate, CorrelationSummary Global, IReadOnlyList<ExceptionCustomerRow> Customers, IReadOnlyList<string> Notes, DateTimeOffset GeneratedAt);

sealed class ConveyorDataService(DashboardConfig config)
{
    private const string RollupCtes = """
        repeated_chute AS (
            SELECT conveyor_key, parcel_id
            FROM raw
            WHERE chute_no IS NOT NULL AND chute_no <> 98
            GROUP BY conveyor_key, parcel_id, chute_no
            HAVING COUNT(*) >= 2
        ),
        chute_sequence AS (
            SELECT r.conveyor_key, r.parcel_id, r.chute_no, r.event_time, r.operational_hour,
                ROW_NUMBER() OVER (
                    PARTITION BY r.conveyor_key, r.parcel_id, r.chute_no
                    ORDER BY r.event_time
                ) AS chute_occurrence
            FROM raw r
            WHERE r.chute_no IS NOT NULL AND r.chute_no <> 98
        ),
        repeat_candidates AS (
            SELECT cs.*,
                ROW_NUMBER() OVER (
                    PARTITION BY cs.conveyor_key, cs.parcel_id
                    ORDER BY cs.event_time
                ) AS parcel_repeat_rank
            FROM chute_sequence cs
            WHERE cs.chute_occurrence = 2
        ),
        repeat_moment AS (
            SELECT conveyor_key, parcel_id, operational_hour, event_time
            FROM repeat_candidates
            WHERE parcel_repeat_rank = 1
        ),
        repeated_parcel AS (
            SELECT DISTINCT conveyor_key, parcel_id FROM repeated_chute
        ),
        basic_rollup AS (
            SELECT
                r.conveyor_key,
                r.conveyor_name,
                r.supports_measurements,
                r.parcel_id,
                MAX(NULLIF(r.customer_id, 0)) AS history_customer_id,
                COUNT(*) AS passages,
                MIN(r.event_time) AS first_passage,
                MAX(r.event_time) AS last_passage,
                MAX(CASE WHEN r.chute_no = 98 THEN 1 ELSE 0 END) AS chute_98,
                MAX(r.weight_value IS NULL OR r.weight_value <= 0) AS no_weight,
                MAX(r.length_value IS NULL OR r.length_value <= 0 OR r.width_value IS NULL OR r.width_value <= 0 OR r.height_value IS NULL OR r.height_value <= 0) AS no_dimensions,
                CAST(SUBSTRING_INDEX(GROUP_CONCAT(CASE WHEN r.weight_value > 0 THEN r.weight_value END ORDER BY r.event_time DESC), ',', 1) AS DECIMAL(12,3)) AS latest_weight,
                CAST(SUBSTRING_INDEX(GROUP_CONCAT(CASE WHEN r.length_value > 0 THEN r.length_value END ORDER BY r.event_time DESC), ',', 1) AS DECIMAL(12,3)) AS latest_length,
                CAST(SUBSTRING_INDEX(GROUP_CONCAT(CASE WHEN r.width_value > 0 THEN r.width_value END ORDER BY r.event_time DESC), ',', 1) AS DECIMAL(12,3)) AS latest_width,
                CAST(SUBSTRING_INDEX(GROUP_CONCAT(CASE WHEN r.height_value > 0 THEN r.height_value END ORDER BY r.event_time DESC), ',', 1) AS DECIMAL(12,3)) AS latest_height,
                GROUP_CONCAT(DATE_FORMAT(r.event_time, '%Y-%m-%d %H:%i:%s') ORDER BY r.event_time SEPARATOR ' | ') AS passage_times,
                GROUP_CONCAT(COALESCE(CAST(r.chute_no AS CHAR), 'NULL') ORDER BY r.event_time SEPARATOR ' | ') AS chutes
            FROM raw r
            GROUP BY r.conveyor_key, r.conveyor_name, r.supports_measurements, r.parcel_id
        ),
        parcel_ref AS (
            SELECT p.PARCEL_ID AS parcel_id, MAX(NULLIF(p.CUSTOMER_ID, 0)) AS customer_id
            FROM parcel p
            JOIN (SELECT DISTINCT parcel_id FROM raw) scope_parcel ON scope_parcel.parcel_id = p.PARCEL_ID
            GROUP BY p.PARCEL_ID
        ),
        rollup AS (
            SELECT b.*, rp.parcel_id IS NOT NULL AS same_chute_repeated,
                COALESCE(b.history_customer_id, pr.customer_id) AS customer_id
            FROM basic_rollup b
            LEFT JOIN repeated_parcel rp ON rp.conveyor_key = b.conveyor_key AND rp.parcel_id = b.parcel_id
            LEFT JOIN parcel_ref pr ON pr.parcel_id = b.parcel_id
        )
        """;

    public async Task<bool> PingAsync()
    {
        await using var connection = new MySqlConnection(config.ConnectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand("SELECT 1", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 1;
    }

    public async Task<AvailableRange> GetAvailableRangeAsync()
    {
        const string sql = "SELECT MIN(DATE(DATE_LIV)), MAX(DATE(DATE_LIV)) FROM parcel_history WHERE EXCEPTION=903 AND SOURCE_TYPE=200 AND DEPOT_ID IN (1,2,12,28) AND DATE_INSERT >= CURDATE() - INTERVAL 2 YEAR";
        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 300 };
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new AvailableRange(reader.IsDBNull(0) ? null : DateOnly.FromDateTime(reader.GetDateTime(0)), reader.IsDBNull(1) ? null : DateOnly.FromDateTime(reader.GetDateTime(1)));
    }

    public async Task<DailyResponse> GetDailyAsync(DateOnly date, string conveyorKey)
    {
        var definitions = ConveyorCatalog.Resolve(conveyorKey);
        var (scopeSql, scopeParameters) = BuildRawScope(date, definitions);
        var sql = $"""
            WITH raw AS ({scopeSql}),
            {RollupCtes}
            SELECT 'metric' AS row_kind, r.conveyor_key, r.conveyor_name, NULL AS operational_hour, NULL AS bucket_start,
                COUNT(*) AS unique_parcels, SUM(r.passages) AS passages,
                SUM(r.passages > 1) AS recirculated, SUM(r.chute_98) AS chute_98, SUM(r.same_chute_repeated) AS same_chute_repeated,
                CASE WHEN MAX(r.supports_measurements)=0 THEN NULL ELSE SUM(r.no_weight) END AS no_weight,
                CASE WHEN MAX(r.supports_measurements)=0 THEN NULL ELSE SUM(r.no_dimensions) END AS no_dimensions,
                CASE WHEN MAX(r.supports_measurements)=0 THEN NULL ELSE SUM(CASE WHEN r.latest_weight > 0 AND r.latest_weight < 1 THEN 1 ELSE 0 END) END AS under_1,
                CASE WHEN MAX(r.supports_measurements)=0 THEN NULL ELSE SUM(CASE WHEN r.latest_weight >= 1 AND r.latest_weight <= 3 THEN 1 ELSE 0 END) END AS from_1_to_3,
                CASE WHEN MAX(r.supports_measurements)=0 THEN NULL ELSE SUM(CASE WHEN r.latest_weight > 3 AND r.latest_weight <= 5 THEN 1 ELSE 0 END) END AS over_3_to_5,
                CASE WHEN MAX(r.supports_measurements)=0 THEN NULL ELSE SUM(CASE WHEN r.latest_weight > 5 AND r.latest_weight <= 10 THEN 1 ELSE 0 END) END AS over_5_to_10,
                CASE WHEN MAX(r.supports_measurements)=0 THEN NULL ELSE SUM(CASE WHEN r.latest_weight > 10 THEN 1 ELSE 0 END) END AS over_10
            FROM rollup r GROUP BY r.conveyor_key, r.conveyor_name
            UNION ALL
            SELECT 'hour', raw.conveyor_key, MAX(raw.conveyor_name), raw.operational_hour, MIN(raw.bucket_start),
                COUNT(DISTINCT raw.parcel_id), COUNT(*), 0,0,COUNT(DISTINCT rm.parcel_id),NULL,NULL,NULL,NULL,NULL,NULL,NULL
            FROM raw
            LEFT JOIN repeat_moment rm
              ON rm.conveyor_key = raw.conveyor_key
             AND rm.parcel_id = raw.parcel_id
             AND rm.operational_hour = raw.operational_hour
            GROUP BY raw.conveyor_key, raw.operational_hour
            ORDER BY conveyor_key, row_kind DESC, operational_hour
            """;
        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 300 };
        AddParameters(command, scopeParameters);
        var metrics = new List<DailyMetric>();
        var hourly = new List<HourPoint>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetString("row_kind") == "hour")
            {
                hourly.Add(new HourPoint(reader.GetString("conveyor_key"), reader.GetInt32("operational_hour"), reader.GetDateTime("bucket_start"), reader.GetInt64("passages"), reader.GetInt64("unique_parcels"), reader.GetInt64("same_chute_repeated")));
                continue;
            }
            var key = reader.GetString("conveyor_key");
            var definition = definitions.First(x => x.Key == key);
            metrics.Add(new DailyMetric(key, reader.GetString("conveyor_name"), definition.Site, definition.SupportsMeasurements,
                reader.GetInt64("unique_parcels"), reader.GetInt64("passages"), reader.GetInt64("recirculated"), reader.GetInt64("chute_98"), reader.GetInt64("same_chute_repeated"),
                NullableInt64(reader, "no_weight"), NullableInt64(reader, "no_dimensions"), NullableInt64(reader, "under_1"), NullableInt64(reader, "from_1_to_3"), NullableInt64(reader, "over_3_to_5"), NullableInt64(reader, "over_5_to_10"), NullableInt64(reader, "over_10")));
        }
        return new DailyResponse(date, metrics, hourly,
        [
            "Un colis est compté une seule fois par convoyeur et journée opérationnelle.",
            "Sans poids / sans dimensions : au moins un passage du colis contient une valeur absente ou non positive.",
            "Gilmore est exclu des KPI de poids et de dimensions parce que ces mesures n'y sont jamais produites.",
        ], DateTimeOffset.Now);
    }

    public async Task<DetailResponse> GetDetailsAsync(DateOnly date, string conveyorKey, string metric, int page, int pageSize, int? customerId)
    {
        var definitions = ConveyorCatalog.Resolve(conveyorKey);
        var allowedMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "total", "recirculated", "chute98", "sameChute", "noWeight", "noDimensions" };
        if (!allowedMetrics.Contains(metric)) throw new ArgumentException("Indicateur de détail inconnu.");
        if ((metric.Equals("noWeight", StringComparison.OrdinalIgnoreCase) || metric.Equals("noDimensions", StringComparison.OrdinalIgnoreCase)) && definitions.All(x => !x.SupportsMeasurements))
            return new DetailResponse(date, metric, page, pageSize, 0, []);
        var predicate = metric.ToLowerInvariant() switch
        {
            "recirculated" => "r.passages > 1",
            "chute98" => "r.chute_98 = 1",
            "samechute" => "r.same_chute_repeated = 1",
            "noweight" => "r.supports_measurements = 1 AND r.no_weight = 1",
            "nodimensions" => "r.supports_measurements = 1 AND r.no_dimensions = 1",
            _ => "1=1",
        };
        var customerPredicate = customerId is null ? "" : " AND r.customer_id = @customerId";
        var (scopeSql, scopeParameters) = BuildRawScope(date, definitions);
        var sql = $"""
            WITH raw AS ({scopeSql}),
            {RollupCtes},
            filtered AS (SELECT r.* FROM rollup r WHERE {predicate}{customerPredicate})
            SELECT f.*, c.NAME AS customer_name, COUNT(*) OVER() AS total_rows
            FROM filtered f LEFT JOIN customer c ON c.CUSTOMER_ID=f.customer_id
            ORDER BY f.passages DESC, f.last_passage DESC
            LIMIT @limit OFFSET @offset
            """;
        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 300 };
        AddParameters(command, scopeParameters);
        command.Parameters.AddWithValue("@limit", pageSize);
        command.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
        if (customerId is not null) command.Parameters.AddWithValue("@customerId", customerId.Value);
        var rows = new List<ParcelDetail>();
        long total = 0;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            total = reader.GetInt64("total_rows");
            var customer = NullableInt32(reader, "customer_id");
            rows.Add(new ParcelDetail(reader.GetInt64("parcel_id"), customer, IsNull(reader, "customer_name") ? (customer is null ? "Client non identifié" : $"Client {customer}") : reader.GetString("customer_name"),
                reader.GetString("conveyor_key"), reader.GetString("conveyor_name"), reader.GetInt64("passages"), reader.GetDateTime("first_passage"), reader.GetDateTime("last_passage"), reader.GetString("passage_times"), reader.GetString("chutes"),
                NullableDecimal(reader, "latest_weight"), NullableDecimal(reader, "latest_length"), NullableDecimal(reader, "latest_width"), NullableDecimal(reader, "latest_height"),
                reader.GetInt64("passages") > 1, reader.GetBoolean("chute_98"), reader.GetBoolean("same_chute_repeated"), reader.GetBoolean("no_weight"), reader.GetBoolean("no_dimensions")));
        }
        return new DetailResponse(date, metric, page, pageSize, total, rows);
    }

    public async Task<CustomerResponse> GetCustomersAsync(DateOnly date, string conveyorKey, int minimumVolume, string sort, string? search)
    {
        var definitions = ConveyorCatalog.Resolve(conveyorKey);
        var (scopeSql, scopeParameters) = BuildRawScope(date, definitions);
        var order = sort.ToLowerInvariant() switch
        {
            "volume" => "total_parcels_given DESC",
            "count" => "problem_parcels DESC",
            "recirculation" => "recirculation_rate DESC",
            "noweight" => "no_weight_rate DESC",
            "nodimensions" => "no_dimensions_rate DESC",
            _ => "problem_vs_total_rate DESC",
        };
        var searchPredicate = string.IsNullOrWhiteSpace(search) ? "" : " AND (c.NAME LIKE @search OR CAST(x.customer_id AS CHAR) LIKE @search)";
        var supportsMeasurements = definitions.Any(x => x.SupportsMeasurements);
        var sql = $"""
            WITH raw AS ({scopeSql}),
            {RollupCtes},
            client_rollup AS (
                SELECT r.customer_id,
                    COUNT(*) AS conveyor_parcels,
                    SUM(CASE WHEN r.supports_measurements THEN 1 ELSE 0 END) AS measurement_parcels,
                    SUM(CASE WHEN r.passages > 1 OR r.chute_98 OR r.same_chute_repeated OR (r.supports_measurements AND (r.no_weight OR r.no_dimensions)) THEN 1 ELSE 0 END) AS problem_parcels,
                    SUM(CASE WHEN r.passages > 1 THEN 1 ELSE 0 END) AS recirculated,
                    SUM(CASE WHEN r.chute_98 THEN 1 ELSE 0 END) AS chute_98,
                    SUM(CASE WHEN r.same_chute_repeated THEN 1 ELSE 0 END) AS same_chute_repeated,
                    SUM(CASE WHEN r.supports_measurements AND r.no_weight THEN 1 ELSE 0 END) AS no_weight,
                    SUM(CASE WHEN r.supports_measurements AND r.no_dimensions THEN 1 ELSE 0 END) AS no_dimensions,
                    SUM(CASE WHEN r.supports_measurements AND r.latest_weight > 0 THEN 1 ELSE 0 END) AS valid_weight,
                    SUM(CASE WHEN r.supports_measurements AND r.latest_weight > 0 AND r.latest_weight < 1 THEN 1 ELSE 0 END) AS under_1,
                    SUM(CASE WHEN r.supports_measurements AND r.latest_weight >= 1 AND r.latest_weight <= 3 THEN 1 ELSE 0 END) AS from_1_to_3,
                    SUM(CASE WHEN r.supports_measurements AND r.latest_weight > 3 AND r.latest_weight <= 5 THEN 1 ELSE 0 END) AS over_3_to_5,
                    SUM(CASE WHEN r.supports_measurements AND r.latest_weight > 5 AND r.latest_weight <= 10 THEN 1 ELSE 0 END) AS over_5_to_10,
                    SUM(CASE WHEN r.supports_measurements AND r.latest_weight > 10 THEN 1 ELSE 0 END) AS over_10,
                    SUM(CASE WHEN r.supports_measurements AND r.latest_length > 0 AND r.latest_width > 0 AND r.latest_height > 0 THEN 1 ELSE 0 END) AS valid_dimensions,
                    SUM(CASE WHEN r.supports_measurements AND r.latest_length > 0 AND r.latest_width > 0 AND r.latest_height > 0 AND r.latest_length*r.latest_width*r.latest_height < 96 THEN 1 ELSE 0 END) AS very_small_format,
                    SUM(CASE WHEN r.supports_measurements AND r.latest_length > 0 AND r.latest_width > 0 AND r.latest_height > 0 AND GREATEST(r.latest_length,r.latest_width,r.latest_height)/LEAST(r.latest_length,r.latest_width,r.latest_height) >= 5 THEN 1 ELSE 0 END) AS atypical_format
                FROM rollup r WHERE r.customer_id IS NOT NULL GROUP BY r.customer_id
            ),
            client_exp_dates AS (
                SELECT DISTINCT r.customer_id, p.EXP_DATE
                FROM rollup r
                JOIN parcel p ON p.PARCEL_ID=r.parcel_id AND p.CUSTOMER_ID=r.customer_id
                WHERE r.customer_id IS NOT NULL AND p.EXP_DATE IS NOT NULL
            ),
            parcel_totals AS (
                SELECT ced.customer_id, COUNT(DISTINCT p.PARCEL_ID) AS total_parcels_given
                FROM client_exp_dates ced
                JOIN parcel p ON p.CUSTOMER_ID=ced.customer_id AND p.EXP_DATE=ced.EXP_DATE
                GROUP BY ced.customer_id
            ),
            combined AS (
                SELECT cr.*, GREATEST(COALESCE(pt.total_parcels_given,0),cr.conveyor_parcels) AS total_parcels_given,
                    cr.problem_parcels/NULLIF(GREATEST(COALESCE(pt.total_parcels_given,0),cr.conveyor_parcels),0) AS problem_vs_total_rate,
                    cr.recirculated/NULLIF(cr.conveyor_parcels,0) AS recirculation_rate,
                    cr.no_weight/NULLIF(cr.measurement_parcels,0) AS no_weight_rate,
                    cr.no_dimensions/NULLIF(cr.measurement_parcels,0) AS no_dimensions_rate
                FROM client_rollup cr LEFT JOIN parcel_totals pt ON pt.customer_id=cr.customer_id
            )
            SELECT x.*, c.NAME AS customer_name,
                (SELECT COUNT(*) FROM rollup WHERE customer_id IS NULL) AS parcels_without_customer,
                (SELECT COUNT(*) FROM rollup WHERE customer_id IS NOT NULL) AS parcels_with_customer
            FROM combined x LEFT JOIN customer c ON c.CUSTOMER_ID=x.customer_id
            WHERE x.total_parcels_given >= @minimumVolume {searchPredicate}
            ORDER BY {order}, x.problem_parcels DESC LIMIT 250
            """;
        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 300 };
        AddParameters(command, scopeParameters);
        command.Parameters.AddWithValue("@minimumVolume", minimumVolume);
        if (!string.IsNullOrWhiteSpace(search)) command.Parameters.AddWithValue("@search", $"%{search.Trim()}%");
        var rows = new List<CustomerRiskRow>();
        long withCustomer = 0, withoutCustomer = 0;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            withCustomer = Int64OrZero(reader, "parcels_with_customer");
            withoutCustomer = Int64OrZero(reader, "parcels_without_customer");
            var id = reader.GetInt32("customer_id");
            rows.Add(new CustomerRiskRow(id, IsNull(reader, "customer_name") ? $"Client {id}" : reader.GetString("customer_name"), Int64OrZero(reader, "total_parcels_given"), Int64OrZero(reader, "conveyor_parcels"), Int64OrZero(reader, "measurement_parcels"), Int64OrZero(reader, "problem_parcels"), Int64OrZero(reader, "recirculated"), Int64OrZero(reader, "chute_98"), Int64OrZero(reader, "same_chute_repeated"),
                supportsMeasurements ? Int64OrZero(reader, "no_weight") : null, supportsMeasurements ? Int64OrZero(reader, "no_dimensions") : null,
                Int64OrZero(reader, "valid_weight"), Int64OrZero(reader, "under_1"), Int64OrZero(reader, "from_1_to_3"), Int64OrZero(reader, "over_3_to_5"), Int64OrZero(reader, "over_5_to_10"), Int64OrZero(reader, "over_10"), Int64OrZero(reader, "valid_dimensions"), Int64OrZero(reader, "very_small_format"), Int64OrZero(reader, "atypical_format")));
        }
        return new CustomerResponse(date, conveyorKey, minimumVolume, withCustomer, withoutCustomer, rows,
        [
            "Impact global = colis problématiques convoyeur / colis totaux du client pour les dates d'expédition représentées parmi ses colis convoyeur de la journée.",
            "Très petit format est un indicateur exploratoire : volume valide inférieur à 96 unités³. Format atypique : rapport du plus grand au plus petit côté ≥ 5.",
            "Ces signaux orientent une vérification d'emballage; ils ne prouvent pas à eux seuls une cause.",
        ], DateTimeOffset.Now);
    }

    public async Task<Exception25Response> GetException25Async(DateOnly endDate, string conveyorKey)
    {
        var definitions = ConveyorCatalog.Resolve(conveyorKey);
        var startDate = endDate.AddDays(-6);
        var scopeParts = new List<string>();
        var allParameters = new List<(string, object)>();
        for (var day = startDate; day <= endDate; day = day.AddDays(1))
        {
            var (sql, parameters) = BuildRawScope(day, definitions, $"d{day.DayNumber}");
            scopeParts.Add(sql);
            allParameters.AddRange(parameters);
        }
        var rawScope = string.Join(" UNION ALL ", scopeParts);
        var earliestStart = definitions.Min(d => startDate.ToDateTime(new TimeOnly(d.StartHour, 0)));
        var latestEnd = definitions.Max(d => endDate.AddDays(1).ToDateTime(new TimeOnly(d.EndHour, 0)));
        var sqlText = $"""
            WITH raw AS ({rawScope}),
            {RollupCtes},
            conveyor_parcels AS (
                SELECT parcel_id, MAX(customer_id) customer_id,
                    MAX(passages > 1 OR chute_98 OR same_chute_repeated OR (supports_measurements AND (no_weight OR no_dimensions))) AS is_problem
                FROM rollup GROUP BY parcel_id
            ),
            exception_25 AS (
                SELECT ph.PARCEL_ID AS parcel_id
                FROM parcel_history ph
                WHERE ph.EXCEPTION=25 AND ph.PARCEL_ID IS NOT NULL AND ph.PARCEL_ID<>0
                  AND ph.DATE_INSERT >= @exceptionInsertStart AND ph.DATE_INSERT < @exceptionInsertEnd
                  AND ph.DATE_LIV >= @exceptionStart AND ph.DATE_LIV < @exceptionEnd
                  AND COALESCE(ph.VOID,0)=0
                GROUP BY ph.PARCEL_ID
            ),
            linked AS (
                SELECT cp.*, e.parcel_id IS NOT NULL AS has_exception_25
                FROM conveyor_parcels cp LEFT JOIN exception_25 e ON e.parcel_id=cp.parcel_id
            )
            SELECT 'global' row_kind, NULL customer_id, NULL customer_name,
                SUM(has_exception_25) exception_count, SUM(has_exception_25 AND is_problem) exception_problem,
                SUM(NOT has_exception_25) no_exception_count, SUM(NOT has_exception_25 AND is_problem) no_exception_problem,
                COUNT(*) conveyor_parcels, SUM(is_problem) problem_parcels
            FROM linked
            UNION ALL
            SELECT 'customer', l.customer_id, COALESCE(c.NAME,CONCAT('Client ',l.customer_id)),
                SUM(l.has_exception_25), SUM(l.has_exception_25 AND l.is_problem), 0,0,COUNT(*),SUM(l.is_problem)
            FROM linked l LEFT JOIN customer c ON c.CUSTOMER_ID=l.customer_id
            WHERE l.customer_id IS NOT NULL
            GROUP BY l.customer_id,c.NAME HAVING SUM(l.has_exception_25)>0
            ORDER BY CASE WHEN row_kind='global' THEN 0 ELSE 1 END, exception_count DESC LIMIT 101
            """;
        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sqlText, connection) { CommandTimeout = 300 };
        AddParameters(command, allParameters);
        command.Parameters.AddWithValue("@exceptionStart", earliestStart);
        command.Parameters.AddWithValue("@exceptionEnd", latestEnd);
        command.Parameters.AddWithValue("@exceptionInsertStart", earliestStart.AddDays(-1));
        command.Parameters.AddWithValue("@exceptionInsertEnd", latestEnd.AddDays(1));
        CorrelationSummary global = new(0, 0, 0, 0);
        var customers = new List<ExceptionCustomerRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetString("row_kind") == "global")
                global = new(reader.GetInt64("exception_count"), reader.GetInt64("exception_problem"), reader.GetInt64("no_exception_count"), reader.GetInt64("no_exception_problem"));
            else
                customers.Add(new ExceptionCustomerRow(reader.GetInt32("customer_id"), reader.GetString("customer_name"), reader.GetInt64("conveyor_parcels"), reader.GetInt64("problem_parcels"), reader.GetInt64("exception_count"), reader.GetInt64("exception_problem")));
        }
        return new Exception25Response(startDate, endDate, global, customers,
        [
            "Fenêtre de sept journées opérationnelles complètes se terminant à la date choisie.",
            "Lien fort : ratio ≥ 2 et écart ≥ 10 points; lien modéré : ratio ≥ 1,5 et écart ≥ 5 points; au moins 100 colis dans chaque groupe.",
            "Il s'agit d'une association statistique, pas d'une preuve de causalité.",
        ], DateTimeOffset.Now);
    }

    private static (string Sql, List<(string Name, object Value)> Parameters) BuildRawScope(DateOnly date, IReadOnlyList<ConveyorDefinition> definitions, string prefix = "s")
    {
        var branches = new List<string>();
        var parameters = new List<(string, object)>();
        for (var i = 0; i < definitions.Count; i++)
        {
            var d = definitions[i];
            var p = $"{prefix}{i}";
            var start = date.ToDateTime(new TimeOnly(d.StartHour, 0));
            var end = date.AddDays(1).ToDateTime(new TimeOnly(d.EndHour, 0));
            branches.Add($"""
                SELECT '{d.Key}' conveyor_key, '{d.Name.Replace("'", "''")}' conveyor_name, {(d.SupportsMeasurements ? 1 : 0)} supports_measurements,
                    ph.PARCEL_ID parcel_id, ph.CUSTOMER_ID customer_id, ph.DATE_LIV event_time,
                    TIMESTAMPDIFF(HOUR,@{p}Start,ph.DATE_LIV) operational_hour,
                    DATE_ADD(@{p}Start, INTERVAL TIMESTAMPDIFF(HOUR,@{p}Start,ph.DATE_LIV) HOUR) bucket_start,
                    ph.CHUTE_NO chute_no, ph.WEIGHT weight_value, ph.LENGTH length_value, ph.WIDTH width_value, ph.HEIGHT height_value
                FROM parcel_history ph
                WHERE ph.EXCEPTION=903 AND ph.SOURCE_TYPE=200 AND ph.DEPOT_ID={d.DepotId} AND {d.SourcePredicate}
                  AND ph.PARCEL_ID IS NOT NULL AND ph.PARCEL_ID<>0 AND COALESCE(ph.VOID,0)=0
                  AND ph.DATE_INSERT>=@{p}InsertStart AND ph.DATE_INSERT<@{p}InsertEnd
                  AND ph.DATE_LIV>=@{p}Start AND ph.DATE_LIV<@{p}End
                """);
            parameters.Add(($"@{p}Start", start));
            parameters.Add(($"@{p}End", end));
            parameters.Add(($"@{p}InsertStart", start.AddDays(-1)));
            parameters.Add(($"@{p}InsertEnd", end.AddDays(1)));
        }
        return (string.Join(" UNION ALL ", branches), parameters);
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var connection = new MySqlConnection(config.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static void AddParameters(MySqlCommand command, IEnumerable<(string Name, object Value)> parameters)
    {
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
    }

    private static bool IsNull(MySqlDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name));
    private static long Int64OrZero(MySqlDataReader reader, string name) => IsNull(reader, name) ? 0 : reader.GetInt64(name);
    private static long? NullableInt64(MySqlDataReader reader, string name) => IsNull(reader, name) ? null : reader.GetInt64(name);
    private static int? NullableInt32(MySqlDataReader reader, string name) => IsNull(reader, name) ? null : reader.GetInt32(name);
    private static decimal? NullableDecimal(MySqlDataReader reader, string name) => IsNull(reader, name) ? null : reader.GetDecimal(name);
}

sealed class OpenAiAnalysisService(HttpClient httpClient, DashboardConfig config)
{
    public async Task<string> AnalyzeAsync(DailyResponse daily, CustomerResponse customers)
    {
        var aliases = customers.Customers.Take(20).Select((customer, index) => new
        {
            Alias = $"Client-{index + 1:00}",
            Customer = customer,
        }).ToList();
        var safePayload = new
        {
            daily.OperationalDate,
            Conveyors = daily.Metrics,
            Customers = aliases.Select(x => new
            {
                x.Alias,
                x.Customer.TotalParcelsGiven,
                x.Customer.ConveyorParcels,
                x.Customer.ProblemParcels,
                x.Customer.ProblemVsTotalRate,
                x.Customer.ProblemOnConveyorRate,
                x.Customer.RecirculationRate,
                x.Customer.Chute98Rate,
                x.Customer.SameChuteRepeatedRate,
                x.Customer.NoWeightRate,
                x.Customer.NoDimensionsRate,
                x.Customer.VeryLightRate,
                x.Customer.VerySmallFormatRate,
                x.Customer.AtypicalFormatRate,
            }),
            Definitions = daily.Notes.Concat(customers.Notes),
        };
        var prompt = """
            Tu es un analyste des opérations de tri de colis. Analyse les agrégats fournis en français canadien.
            Donne : 1) les constats prioritaires, 2) les clients à vérifier avec les indicateurs qui justifient la priorité,
            3) les hypothèses plausibles liées au poids, au petit format, au format atypique ou à l'emballage,
            4) les vérifications terrain recommandées. Distingue clairement constat, hypothèse et causalité non démontrée.
            Utilise les pourcentages et dénominateurs. Ne fusionne jamais St-Hubert haut et St-Hubert sol : compare-les séparément.
            Gilmore est non applicable pour poids/dimensions.

            DONNÉES AGRÉGÉES :
            """ + JsonSerializer.Serialize(safePayload);
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.OpenAiApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = config.OpenAiModel,
            input = prompt,
            max_output_tokens = 2200,
        }), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            string message;
            try { message = JsonDocument.Parse(json).RootElement.GetProperty("error").GetProperty("message").GetString() ?? "La requête OpenAI a échoué."; }
            catch { message = "La requête OpenAI a échoué."; }
            throw new OpenAiException(message, (int)response.StatusCode);
        }
        using var document = JsonDocument.Parse(json);
        var text = document.RootElement.GetProperty("output").EnumerateArray()
            .SelectMany(item => item.TryGetProperty("content", out var content) ? content.EnumerateArray().ToArray() : [])
            .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "output_text")
            .Select(item => item.GetProperty("text").GetString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? throw new OpenAiException("OpenAI n'a retourné aucun texte d'analyse.", 502);
        foreach (var item in aliases.OrderByDescending(x => x.Alias.Length))
            text = text.Replace(item.Alias, $"{item.Customer.CustomerName} ({item.Customer.CustomerId})", StringComparison.Ordinal);
        return text;
    }
}

sealed class OpenAiException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
