using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);

LoadEnvironmentFile(Path.Combine(builder.Environment.ContentRootPath, ".env.local"));
LoadEnvironmentFile(Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "MySqlTool", ".env.local")));

var config = new DashboardConfig(
    Environment.GetEnvironmentVariable("MYSQL_HOST") ?? "192.168.1.101",
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

app.MapGet("/api/live-routes", async (ConveyorDataService data) =>
{
    try { return Results.Ok(await data.GetLiveRoutesAsync()); }
    catch (Exception ex) { return Results.Problem($"Le suivi des routes n'a pas pu être calculé : {ex.Message}"); }
});

app.MapGet("/api/live-routes/{routeId:int}/clients", async (int routeId, ConveyorDataService data) =>
{
    if (routeId is < 50000 or > 50099) return Results.BadRequest(new { error = "La route doit être comprise entre 50000 et 50099." });
    try { return Results.Ok(await data.GetLiveRouteClientsAsync(routeId)); }
    catch (Exception ex) { return Results.Problem($"Le détail des clients n'a pas pu être calculé : {ex.Message}"); }
});

app.MapGet("/api/unprocessed-parcels", async (ConveyorDataService data) =>
{
    try { return Results.Ok(await data.GetUnprocessedParcelsAsync()); }
    catch (Exception ex) { return Results.Problem($"La liste des colis non traités n'a pas pu être calculée : {ex.Message}"); }
});

app.MapGet("/api/conveyor-hourly", async (string? date, ConveyorDataService data) =>
{
    try { return Results.Ok(await data.GetConveyorHourlyAsync(ResolveAnalysisDate(date, CurrentOperationalDate(DateTime.Now)))); }
    catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
    catch (Exception ex) { return Results.Problem($"Les volumes horaires du convoyeur n'ont pas pu être calculés : {ex.Message}"); }
});

app.MapGet("/api/conveyor-quality", async (string? date, ConveyorDataService data) =>
{
    try { return Results.Ok(await data.GetConveyorQualityAsync(ResolveAnalysisDate(date, CurrentOperationalDate(DateTime.Now)))); }
    catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
    catch (Exception ex) { return Results.Problem($"Les indicateurs de qualité du convoyeur n'ont pas pu être calculés : {ex.Message}"); }
});

app.MapGet("/api/high-conveyor-capacity", async (string? date, ConveyorDataService data) =>
{
    try { return Results.Ok(await data.GetHighConveyorCapacityAsync(ResolveAnalysisDate(date, CurrentOperationalDate(DateTime.Now)))); }
    catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
    catch (Exception ex) { return Results.Problem($"L'analyse de capacité du convoyeur du haut n'a pas pu être calculée : {ex.Message}"); }
});

app.MapGet("/api/scan-depots", async (ConveyorDataService data) =>
{
    try { return Results.Ok(await data.GetScanDepotsAsync()); }
    catch (Exception ex) { return Results.Problem($"La liste des dépôts n'a pas pu être chargée : {ex.Message}"); }
});

app.MapGet("/api/quebec-depot-scans", async (int? depotId, string? date, string? startTime, string? endTime, ConveyorDataService data) =>
{
    var sourceDepotId = depotId ?? 2;
    if (sourceDepotId <= 0) return Results.BadRequest("Le numéro de dépôt doit être supérieur à zéro.");
    try
    {
        var window = ResolveTimeWindow(date, startTime, endTime, DateTime.Now);
        return Results.Ok(await data.GetQuebecDepotScansAsync(sourceDepotId, window.Start, window.End));
    }
    catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
    catch (Exception ex) { return Results.Problem($"Les scans du dépôt n'ont pas pu être calculés : {ex.Message}"); }
});

app.MapGet("/api/quebec-depot-scans/code25-attributed", async (int? depotId, string? date, ConveyorDataService data) =>
{
    var attributionDepotId = depotId ?? 1;
    if (attributionDepotId <= 0) return Results.BadRequest("Le numéro de dépôt doit être supérieur à zéro.");
    try { return Results.Ok(await data.GetAttributedCode25ParcelsAsync(attributionDepotId, ResolveAnalysisDate(date, DateOnly.FromDateTime(DateTime.Now)))); }
    catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
    catch (Exception ex) { return Results.Problem($"Les colis attribués au dépôt n'ont pas pu être chargés : {ex.Message}"); }
});

app.MapGet("/api/quebec-depot-scans/code25-destinations", async (int? depotId, string? date, string? startTime, string? endTime, ConveyorDataService data) =>
{
    var sourceDepotId = depotId ?? 2;
    if (sourceDepotId <= 0) return Results.BadRequest("Le numéro de dépôt doit être supérieur à zéro.");
    try
    {
        var window = ResolveTimeWindow(date, startTime, endTime, DateTime.Now);
        return Results.Ok(await data.GetQuebecCode25DestinationsAsync(sourceDepotId, window.Start, window.End));
    }
    catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
    catch (Exception ex) { return Results.Problem($"Le détail des codes 25 par destination n'a pas pu être calculé : {ex.Message}"); }
});

app.MapGet("/api/quebec-depot-scans/code25-destinations/{depotId:int}/parcels", async (int depotId, int? sourceDepotId, string? date, string? startTime, string? endTime, ConveyorDataService data) =>
{
    if (depotId <= 0) return Results.BadRequest("Le numéro de dépôt doit être supérieur à zéro.");
    var selectedSourceDepotId = sourceDepotId ?? 2;
    if (selectedSourceDepotId <= 0) return Results.BadRequest("Le numéro du dépôt analysé doit être supérieur à zéro.");
    try
    {
        var window = ResolveTimeWindow(date, startTime, endTime, DateTime.Now);
        return Results.Ok(await data.GetQuebecCode25ParcelsAsync(selectedSourceDepotId, depotId, window.Start, window.End));
    }
    catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
    catch (Exception ex) { return Results.Problem($"La liste des colis du dépôt n'a pas pu être calculée : {ex.Message}"); }
});

app.MapGet("/api/parcels/{parcelId:long}/history", async (long parcelId, ConveyorDataService data) =>
{
    if (parcelId <= 0) return Results.BadRequest("Le numéro de colis doit être supérieur à zéro.");
    try { return Results.Ok(await data.GetParcelHistoryAsync(parcelId)); }
    catch (Exception ex) { return Results.Problem($"L'historique du colis n'a pas pu être chargé : {ex.Message}"); }
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

static DateOnly ResolveAnalysisDate(string? value, DateOnly fallback)
{
    if (string.IsNullOrWhiteSpace(value)) return fallback;
    if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)) return result;
    throw new ArgumentException("La date doit être au format AAAA-MM-JJ.");
}

static DateOnly CurrentOperationalDate(DateTime now) =>
    DateOnly.FromDateTime(now.Hour < 4 ? now.AddDays(-1) : now);

static TimeWindowBounds ResolveTimeWindow(string? dateValue, string? startValue, string? endValue, DateTime now)
{
    const string format = "HH:mm";
    var startText = string.IsNullOrWhiteSpace(startValue) ? "00:00" : startValue;
    var endText = string.IsNullOrWhiteSpace(endValue) ? "23:59" : endValue;
    if (!TimeOnly.TryParseExact(startText, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime))
        throw new ArgumentException("L'heure de début doit être au format HH:mm.");
    if (!TimeOnly.TryParseExact(endText, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime))
        throw new ArgumentException("L'heure de fin doit être au format HH:mm.");
    if (startTime == endTime)
        throw new ArgumentException("Les heures de début et de fin doivent être différentes.");

    var analysisDate = ResolveAnalysisDate(dateValue, DateOnly.FromDateTime(now));
    var start = analysisDate.ToDateTime(startTime);
    var end = analysisDate.ToDateTime(endTime);
    if (endText == "23:59") end = end.AddMinutes(1);
    if (endTime < startTime)
        end = end.AddDays(1);
    return new TimeWindowBounds(start, end, startText, endText);
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
sealed record LiveRouteRow(
    int RouteId,
    long ParcelsPassed,
    long ParcelsHigh,
    long ParcelsFloor,
    long ParcelsManual,
    long ParcelsLast5Minutes,
    long ParcelsCreatedToday,
    long HistoricalAverage,
    long EstimatedTotal,
    long EstimatedRemaining,
    decimal EstimatedProgressPercent,
    DateTime? FirstSeen,
    DateTime? LastSeen,
    string Status,
    string Confidence);
sealed record LiveRoutesResponse(
    DateOnly Date,
    DateTime DatabaseNow,
    DateTime? LatestScan,
    long TotalProcessedParcels,
    long TotalHighParcels,
    long TotalFloorParcels,
    long TotalManualParcels,
    DateTime? FirstHighScan,
    DateTime? FirstFloorScan,
    DateTime? FirstManualScan,
    long MappedProcessedParcels,
    long UnmappedProcessedParcels,
    long AmbiguousProcessedParcels,
    IReadOnlyList<LiveRouteRow> Routes,
    IReadOnlyList<string> Notes,
    DateTimeOffset GeneratedAt);
sealed record LiveRouteClientRow(
    int CustomerId,
    string CustomerName,
    string PickupTime,
    string Note,
    long ParcelsPassed,
    long ParcelsHigh,
    long ParcelsFloor,
    long ParcelsManual,
    long ParcelsCreatedToday,
    DateTime? FirstSeen,
    DateTime? LastSeen,
    string Verification);
sealed record LiveRouteClientsResponse(
    int RouteId,
    string ScheduleDay,
    int ScheduledClients,
    int ObservedClients,
    long ParcelsPassed,
    IReadOnlyList<LiveRouteClientRow> Clients,
    IReadOnlyList<string> VerificationSources,
    DateTimeOffset GeneratedAt);
sealed record UnprocessedClientRow(
    int CustomerId,
    string CustomerName,
    string Routes,
    string PickupTime,
    long CreatedToday,
    long CreatedYesterday,
    long CreatedTwoDaysAgo,
    long UnprocessedParcels,
    DateTime OldestCreated,
    DateTime NewestCreated);
sealed record UnprocessedParcelsResponse(
    DateOnly WindowStart,
    DateOnly WindowEnd,
    string ScheduleDay,
    int Clients,
    long UnprocessedParcels,
    IReadOnlyList<UnprocessedClientRow> Rows,
    IReadOnlyList<string> Notes,
    DateTimeOffset GeneratedAt);
sealed record ConveyorHourlyRow(string Source, int Hour, long Parcels);
sealed record ConveyorHourlyResponse(
    DateOnly Date,
    DateTime DatabaseNow,
    DateTime ShiftStart,
    DateTime ShiftEnd,
    long TotalHighParcels,
    long TotalFloorParcels,
    long TotalManualParcels,
    DateTime? FirstHighScan,
    DateTime? FirstFloorScan,
    DateTime? FirstManualScan,
    DateTime? LastHighScan,
    DateTime? LastFloorScan,
    DateTime? LastManualScan,
    IReadOnlyList<ConveyorHourlyRow> Rows,
    IReadOnlyList<string> Notes,
    DateTimeOffset GeneratedAt);
sealed record RecirculationChute(int Chute, long Parcels);
sealed record ConveyorQualityResponse(
    DateOnly Date,
    long TotalConveyed,
    long Chute98,
    long NoRead,
    long SameChuteRecirculated,
    IReadOnlyList<RecirculationChute> TopRecirculationChutes,
    DateTimeOffset GeneratedAt)
{
    private double Rate(long value) => TotalConveyed == 0 ? 0 : 100d * value / TotalConveyed;
    public double Chute98Percent => Rate(Chute98);
    public double NoReadPercent => Rate(NoRead);
    public double SameChuteRecirculatedPercent => Rate(SameChuteRecirculated);
}
sealed record HighCapacityDailyPeak(
    DateOnly ShiftDate,
    long PeakPerHour,
    DateTime PeakWindowStart,
    long TotalParcels);
sealed record HighCapacityBucket(
    DateTime BucketStart,
    long UniqueParcels,
    long TotalParcels,
    long Recirculated,
    long Chute98,
    decimal ParcelsPerHour,
    decimal UtilizationPercent,
    string Status,
    bool IsFuture);
sealed record HighCapacityGap(
    DateTime Start,
    DateTime End,
    int DurationMinutes,
    long Parcels,
    decimal AveragePerHour,
    decimal UtilizationPercent);
sealed record HighConveyorCapacityResponse(
    DateOnly ShiftDate,
    DateTime DatabaseNow,
    DateTime ShiftStart,
    DateTime ShiftEnd,
    int BenchmarkShifts,
    long PracticalCapacityPerHour,
    long MaximumObservedPerHour,
    decimal CurrentRatePerHour,
    decimal AveragePerHourSinceStart,
    decimal UtilizationSinceStartPercent,
    int ActiveMinutes,
    int ExcludedZeroMinutes,
    int PotentialMinutes,
    long PotentialParcelsAtPracticalCapacity,
    int MinutesAtOrAboveCapacity,
    int GapMinutes,
    IReadOnlyList<HighCapacityDailyPeak> DailyPeaks,
    IReadOnlyList<HighCapacityBucket> Buckets,
    IReadOnlyList<HighCapacityGap> Gaps,
    IReadOnlyList<string> Notes,
    DateTimeOffset GeneratedAt);
sealed record CapacityBenchmarkSnapshot(
    DateTimeOffset LoadedAt,
    long PracticalCapacityPerHour,
    long MaximumObservedPerHour,
    IReadOnlyList<HighCapacityDailyPeak> DailyPeaks);
sealed record QuebecScanHourlyRow(
    int Hour,
    DateTime BucketStart,
    long Conveyor903,
    long Floor904,
    long Code25);
sealed record ScanDepotOption(
    int DepotId,
    string DepotName,
    string DepotShortLabel,
    bool HasConveyor);
sealed record TimeWindowBounds(DateTime Start, DateTime End, string StartTime, string EndTime);
sealed record QuebecDepotScansResponse(
    DateOnly Date,
    DateTime DatabaseNow,
    DateTime DayStart,
    DateTime DayEnd,
    DateTime? LatestScan,
    long TotalConveyor903,
    long TotalFloor904,
    long TotalCode25,
    long TotalCode25ReroutedElsewhere,
    DateTime Code25AttributionSince,
    IReadOnlyList<QuebecScanHourlyRow> Rows,
    IReadOnlyList<string> Notes,
    DateTimeOffset GeneratedAt);
sealed record QuebecCode25DestinationRow(
    int? DestinationDepotId,
    string DestinationDepotName,
    int RouteCount,
    long Parcels,
    decimal SharePercent,
    DateTime FirstScan,
    DateTime LastScan);
sealed record QuebecCode25DestinationsResponse(
    DateOnly Date,
    DateTime DatabaseNow,
    long TotalCode25,
    IReadOnlyList<QuebecCode25DestinationRow> Destinations,
    IReadOnlyList<string> Notes,
    DateTimeOffset GeneratedAt);
sealed record QuebecCode25ParcelRow(
    long ParcelId,
    int? CustomerId,
    string CustomerName,
    int? DestinationSectorId,
    decimal? PreviousWeight,
    decimal? PreviousLength,
    decimal? PreviousWidth,
    decimal? PreviousHeight,
    DateTime? PreviousScanDate,
    int? PreviousScanCode,
    DateTime ScanTime);
sealed record QuebecCode25ParcelsResponse(
    DateOnly Date,
    DateTime DatabaseNow,
    int DepotId,
    string DepotName,
    long TotalParcels,
    IReadOnlyList<QuebecCode25ParcelRow> Parcels,
    DateTimeOffset GeneratedAt);
sealed record AttributedCode25ParcelRow(
    long ParcelId,
    int? CustomerId,
    string CustomerName,
    DateTime LastConveyorScan,
    DateTime Code25Time,
    int Code25DepotId,
    string Code25DepotName);
sealed record AttributedCode25ParcelsResponse(
    DateTime DatabaseNow,
    DateTime Since,
    int AttributionDepotId,
    string AttributionDepotName,
    long TotalParcels,
    IReadOnlyList<AttributedCode25ParcelRow> Parcels,
    DateTimeOffset GeneratedAt);
sealed record ParcelHistoryEventRow(
    long ParcelHistoryId,
    int ExceptionCode,
    string Description,
    DateTime EventDate,
    string UserOrTpsl,
    int? DepotId,
    string DepotName);
sealed record ParcelHistoryResponse(
    long ParcelId,
    DateTime DatabaseNow,
    string DestinationAddress,
    string DestinationCity,
    IReadOnlyList<ParcelHistoryEventRow> Events,
    DateTimeOffset GeneratedAt);

sealed class ConveyorDataService(DashboardConfig config)
{
    private readonly SemaphoreSlim capacityBenchmarkLock = new(1, 1);
    private CapacityBenchmarkSnapshot? capacityBenchmarkCache;
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

    public async Task<LiveRoutesResponse> GetLiveRoutesAsync()
    {
        const string sql = """
            WITH RECURSIVE
            scheduled_today AS (
                SELECT DISTINCT p.CUSTOMER_ID customer_id, p.ROUTE_ID route_id
                FROM customer_schedule_pickup p
                WHERE p.ROUTE_ID BETWEEN 50000 AND 50099
                  AND CASE DAYOFWEEK(CURDATE())
                        WHEN 1 THEN p.SUNDAY WHEN 2 THEN p.MONDAY WHEN 3 THEN p.TUESDAY
                        WHEN 4 THEN p.WEDNESDAY WHEN 5 THEN p.THURSDAY WHEN 6 THEN p.FRIDAY
                        WHEN 7 THEN p.SATURDAY END = 1
            ),
            route_map_raw AS (
                SELECT customer_id, COUNT(DISTINCT route_id) route_count, MIN(route_id) route_id
                FROM scheduled_today
                GROUP BY customer_id
            ),
            route_map AS (
                SELECT customer_id, route_id FROM route_map_raw WHERE route_count = 1
            ),
            route_list AS (
                SELECT DISTINCT route_id FROM scheduled_today
            ),
            scanned_parcels AS (
                SELECT ph.PARCEL_ID parcel_id, MAX(NULLIF(ph.CUSTOMER_ID,0)) customer_id,
                       MAX(ph.SOURCE_TYPE=200 AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID=1)) passed_high,
                       MAX(ph.SOURCE_TYPE=200 AND ph.SOURCE_ID=3) passed_floor,
                       MAX(ph.SOURCE_TYPE=201) passed_manual,
                       MIN(CASE WHEN ph.SOURCE_TYPE=200 AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID=1) THEN ph.DATE_LIV END) first_high_scan,
                       MIN(CASE WHEN ph.SOURCE_TYPE=200 AND ph.SOURCE_ID=3 THEN ph.DATE_LIV END) first_floor_scan,
                       MIN(CASE WHEN ph.SOURCE_TYPE=201 THEN ph.DATE_LIV END) first_manual_scan,
                       MIN(ph.DATE_LIV) first_seen, MAX(ph.DATE_LIV) last_seen
                FROM parcel_history ph
                WHERE ph.EXCEPTION=903 AND ph.DEPOT_ID=1
                  AND ((ph.SOURCE_TYPE=200 AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID IN (1,3))) OR ph.SOURCE_TYPE=201)
                  AND ph.PARCEL_ID IS NOT NULL AND ph.PARCEL_ID<>0 AND COALESCE(ph.VOID,0)=0
                  AND ph.DATE_INSERT>=CURDATE()+INTERVAL 15 HOUR
                  AND ph.DATE_INSERT<CURDATE()+INTERVAL 1 DAY
                  AND ph.DATE_LIV>=CURDATE()+INTERVAL 16 HOUR
                  AND ph.DATE_LIV<CURDATE()+INTERVAL 1 DAY
                GROUP BY ph.PARCEL_ID
            ),
            passed_by_route AS (
                SELECT rm.route_id, COUNT(*) parcels_passed,
                       SUM(sp.passed_high) parcels_high,
                       SUM(sp.passed_floor) parcels_floor,
                       SUM(sp.passed_manual) parcels_manual,
                       SUM(sp.last_seen>=NOW()-INTERVAL 5 MINUTE) parcels_last_5m,
                       MIN(sp.first_seen) first_seen, MAX(sp.last_seen) last_seen
                FROM scanned_parcels sp JOIN route_map rm ON rm.customer_id=sp.customer_id
                GROUP BY rm.route_id
            ),
            today_created AS (
                SELECT rm.route_id, COALESCE(SUM(s.PARCEL_NB),0) parcels_created_today
                FROM route_map rm JOIN shipment s ON s.CUSTOMER_ID=rm.customer_id
                  AND s.INSERT_DATE>=CURDATE() AND s.INSERT_DATE<CURDATE()+INTERVAL 1 DAY
                GROUP BY rm.route_id
            ),
            history_created AS (
                SELECT rm.route_id, ROUND(COALESCE(SUM(s.PARCEL_NB),0)/4.0) historical_average
                FROM route_map rm JOIN shipment s ON s.CUSTOMER_ID=rm.customer_id
                  AND s.INSERT_DATE>=CURDATE()-INTERVAL 28 DAY AND s.INSERT_DATE<CURDATE()
                  AND DAYOFWEEK(s.INSERT_DATE)=DAYOFWEEK(CURDATE())
                GROUP BY rm.route_id
            ),
            route_metrics AS (
                SELECT rl.route_id,
                       COALESCE(pbr.parcels_passed,0) parcels_passed,
                       COALESCE(pbr.parcels_high,0) parcels_high,
                       COALESCE(pbr.parcels_floor,0) parcels_floor,
                       COALESCE(pbr.parcels_manual,0) parcels_manual,
                       COALESCE(pbr.parcels_last_5m,0) parcels_last_5m,
                       COALESCE(tc.parcels_created_today,0) parcels_created_today,
                       COALESCE(hc.historical_average,0) historical_average,
                       GREATEST(COALESCE(pbr.parcels_passed,0),COALESCE(tc.parcels_created_today,0),COALESCE(hc.historical_average,0)) estimated_total,
                       pbr.first_seen, pbr.last_seen
                FROM route_list rl
                LEFT JOIN passed_by_route pbr ON pbr.route_id=rl.route_id
                LEFT JOIN today_created tc ON tc.route_id=rl.route_id
                LEFT JOIN history_created hc ON hc.route_id=rl.route_id
            )
            SELECT NOW() database_now,
                   (SELECT MAX(last_seen) FROM scanned_parcels) latest_scan,
                   (SELECT COUNT(*) FROM scanned_parcels) total_processed_parcels,
                   (SELECT COALESCE(SUM(passed_high),0) FROM scanned_parcels) total_high_parcels,
                   (SELECT COALESCE(SUM(passed_floor),0) FROM scanned_parcels) total_floor_parcels,
                   (SELECT COALESCE(SUM(passed_manual),0) FROM scanned_parcels) total_manual_parcels,
                   (SELECT MIN(first_high_scan) FROM scanned_parcels) first_high_scan,
                   (SELECT MIN(first_floor_scan) FROM scanned_parcels) first_floor_scan,
                   (SELECT MIN(first_manual_scan) FROM scanned_parcels) first_manual_scan,
                   (SELECT COUNT(*) FROM scanned_parcels sp JOIN route_map rm ON rm.customer_id=sp.customer_id) mapped_processed_parcels,
                   (SELECT COUNT(*) FROM scanned_parcels sp LEFT JOIN route_map_raw rmr ON rmr.customer_id=sp.customer_id WHERE rmr.customer_id IS NULL) unmapped_processed_parcels,
                   (SELECT COUNT(*) FROM scanned_parcels sp JOIN route_map_raw rmr ON rmr.customer_id=sp.customer_id WHERE rmr.route_count>1) ambiguous_processed_parcels,
                   rm.route_id, rm.parcels_passed, rm.parcels_high, rm.parcels_floor, rm.parcels_manual,
                   rm.parcels_last_5m, rm.parcels_created_today,
                   rm.historical_average, rm.estimated_total,
                   GREATEST(rm.estimated_total-rm.parcels_passed,0) estimated_remaining,
                   ROUND(100.0*rm.parcels_passed/NULLIF(rm.estimated_total,0),1) estimated_progress_pct,
                   rm.first_seen, rm.last_seen
            FROM route_metrics rm
            ORDER BY (rm.last_seen>=NOW()-INTERVAL 5 MINUTE) DESC, rm.parcels_last_5m DESC, rm.last_seen DESC, rm.route_id
            """;

        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 90 };
        await using var reader = await command.ExecuteReaderAsync();
        var routes = new List<LiveRouteRow>();
        var databaseNow = DateTime.Now;
        DateTime? latestScan = null;
        DateTime? firstHighScan = null, firstFloorScan = null, firstManualScan = null;
        long total = 0, totalHigh = 0, totalFloor = 0, totalManual = 0, mapped = 0, unmapped = 0, ambiguous = 0;
        while (await reader.ReadAsync())
        {
            databaseNow = reader.GetDateTime("database_now");
            latestScan = IsNull(reader, "latest_scan") ? null : reader.GetDateTime("latest_scan");
            total = Int64OrZero(reader, "total_processed_parcels");
            totalHigh = Int64OrZero(reader, "total_high_parcels");
            totalFloor = Int64OrZero(reader, "total_floor_parcels");
            totalManual = Int64OrZero(reader, "total_manual_parcels");
            firstHighScan = IsNull(reader, "first_high_scan") ? null : reader.GetDateTime("first_high_scan");
            firstFloorScan = IsNull(reader, "first_floor_scan") ? null : reader.GetDateTime("first_floor_scan");
            firstManualScan = IsNull(reader, "first_manual_scan") ? null : reader.GetDateTime("first_manual_scan");
            mapped = Int64OrZero(reader, "mapped_processed_parcels");
            unmapped = Int64OrZero(reader, "unmapped_processed_parcels");
            ambiguous = Int64OrZero(reader, "ambiguous_processed_parcels");
            DateTime? firstSeen = IsNull(reader, "first_seen") ? null : reader.GetDateTime("first_seen");
            DateTime? lastSeen = IsNull(reader, "last_seen") ? null : reader.GetDateTime("last_seen");
            var last5 = Int64OrZero(reader, "parcels_last_5m");
            var createdToday = Int64OrZero(reader, "parcels_created_today");
            var historical = Int64OrZero(reader, "historical_average");
            var status = lastSeen is null ? "pending"
                : lastSeen >= databaseNow.AddMinutes(-5) && last5 >= 2
                ? "active"
                : lastSeen >= databaseNow.AddMinutes(-15) ? "recent" : "inactive";
            var confidence = createdToday > 0 ? "moyenne" : historical > 0 ? "faible" : "très faible";
            routes.Add(new LiveRouteRow(
                reader.GetInt32("route_id"),
                Int64OrZero(reader, "parcels_passed"),
                Int64OrZero(reader, "parcels_high"),
                Int64OrZero(reader, "parcels_floor"),
                Int64OrZero(reader, "parcels_manual"),
                last5,
                createdToday,
                historical,
                Int64OrZero(reader, "estimated_total"),
                Int64OrZero(reader, "estimated_remaining"),
                IsNull(reader, "estimated_progress_pct") ? 0 : reader.GetDecimal("estimated_progress_pct"),
                firstSeen,
                lastSeen,
                status,
                confidence));
        }

        return new LiveRoutesResponse(
            DateOnly.FromDateTime(databaseNow), databaseNow, latestScan, total, totalHigh, totalFloor, totalManual,
            firstHighScan, firstFloorScan, firstManualScan, mapped, unmapped, ambiguous, routes,
            [
                "Périmètre : convoyeurs haut et sol et postes manuels à Saint-Hubert, colis uniques traités depuis 16:00, routes 50000 à 50099.",
                "Toutes les routes 500xx et tous les clients dont le pickup est planifié aujourd'hui sont inclus, même si aucun colis n'a encore été observé.",
                "Estimation totale : maximum entre les colis déjà passés, les colis créés aujourd'hui et la moyenne des quatre mêmes jours de semaine précédents.",
                "Les restants soustraient l'union des colis vus en haut, au sol ou manuellement; un même colis vu par plusieurs sources n'est soustrait qu'une fois.",
                "Les clients associés à plusieurs routes 500xx sont exclus des totaux par route afin d'éviter le double comptage."
            ],
            DateTimeOffset.Now);
    }

    public async Task<LiveRouteClientsResponse> GetLiveRouteClientsAsync(int routeId)
    {
        const string sql = """
            WITH scheduled AS (
                SELECT p.CUSTOMER_ID customer_id,
                       COALESCE(c.NAME,CONCAT('Client ',p.CUSTOMER_ID)) customer_name,
                       COALESCE(GROUP_CONCAT(DISTINCT TIME_FORMAT(p.END_TIME,'%H:%i') ORDER BY p.END_TIME SEPARATOR ' / '),'N/D') pickup_time,
                       COALESCE(GROUP_CONCAT(DISTINCT NULLIF(TRIM(p.NOTE_FR),'') ORDER BY p.NOTE_FR SEPARATOR ' · '),'') note
                FROM customer_schedule_pickup p
                LEFT JOIN customer c ON c.CUSTOMER_ID=p.CUSTOMER_ID
                WHERE p.ROUTE_ID=@routeId
                  AND CASE DAYOFWEEK(CURDATE())
                        WHEN 1 THEN p.SUNDAY WHEN 2 THEN p.MONDAY WHEN 3 THEN p.TUESDAY
                        WHEN 4 THEN p.WEDNESDAY WHEN 5 THEN p.THURSDAY WHEN 6 THEN p.FRIDAY
                        WHEN 7 THEN p.SATURDAY END = 1
                GROUP BY p.CUSTOMER_ID,c.NAME
            ),
            scanned_parcels AS (
                SELECT ph.PARCEL_ID parcel_id, MAX(NULLIF(ph.CUSTOMER_ID,0)) customer_id,
                       MAX(ph.SOURCE_TYPE=200 AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID=1)) passed_high,
                       MAX(ph.SOURCE_TYPE=200 AND ph.SOURCE_ID=3) passed_floor,
                       MAX(ph.SOURCE_TYPE=201) passed_manual,
                       MIN(ph.DATE_LIV) first_seen, MAX(ph.DATE_LIV) last_seen
                FROM parcel_history ph
                WHERE ph.EXCEPTION=903 AND ph.DEPOT_ID=1
                  AND ((ph.SOURCE_TYPE=200 AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID IN (1,3))) OR ph.SOURCE_TYPE=201)
                  AND ph.PARCEL_ID IS NOT NULL AND ph.PARCEL_ID<>0 AND COALESCE(ph.VOID,0)=0
                  AND ph.DATE_INSERT>=CURDATE()+INTERVAL 15 HOUR
                  AND ph.DATE_INSERT<CURDATE()+INTERVAL 1 DAY
                  AND ph.DATE_LIV>=CURDATE()+INTERVAL 16 HOUR
                  AND ph.DATE_LIV<CURDATE()+INTERVAL 1 DAY
                GROUP BY ph.PARCEL_ID
            ),
            passed AS (
                SELECT customer_id,COUNT(*) parcels_passed,
                       SUM(passed_high) parcels_high,SUM(passed_floor) parcels_floor,SUM(passed_manual) parcels_manual,
                       MIN(first_seen) first_seen,MAX(last_seen) last_seen
                FROM scanned_parcels GROUP BY customer_id
            ),
            created AS (
                SELECT s.CUSTOMER_ID customer_id,COALESCE(SUM(s.PARCEL_NB),0) parcels_created_today
                FROM shipment s
                WHERE s.INSERT_DATE>=CURDATE() AND s.INSERT_DATE<CURDATE()+INTERVAL 1 DAY
                GROUP BY s.CUSTOMER_ID
            )
            SELECT sc.customer_id,sc.customer_name,sc.pickup_time,sc.note,
                   COALESCE(p.parcels_passed,0) parcels_passed,
                   COALESCE(p.parcels_high,0) parcels_high,
                   COALESCE(p.parcels_floor,0) parcels_floor,
                   COALESCE(p.parcels_manual,0) parcels_manual,
                   COALESCE(cr.parcels_created_today,0) parcels_created_today,
                   p.first_seen,p.last_seen
            FROM scheduled sc
            LEFT JOIN passed p ON p.customer_id=sc.customer_id
            LEFT JOIN created cr ON cr.customer_id=sc.customer_id
            ORDER BY (COALESCE(p.parcels_passed,0)>0) DESC,p.first_seen,sc.pickup_time,sc.customer_name
            """;

        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 90 };
        command.Parameters.AddWithValue("@routeId", routeId);
        await using var reader = await command.ExecuteReaderAsync();
        var clients = new List<LiveRouteClientRow>();
        while (await reader.ReadAsync())
        {
            var passed = Int64OrZero(reader, "parcels_passed");
            clients.Add(new LiveRouteClientRow(
                reader.GetInt32("customer_id"),
                reader.GetString("customer_name").Trim(),
                reader.GetString("pickup_time"),
                reader.GetString("note"),
                passed,
                Int64OrZero(reader, "parcels_high"),
                Int64OrZero(reader, "parcels_floor"),
                Int64OrZero(reader, "parcels_manual"),
                Int64OrZero(reader, "parcels_created_today"),
                IsNull(reader, "first_seen") ? null : reader.GetDateTime("first_seen"),
                IsNull(reader, "last_seen") ? null : reader.GetDateTime("last_seen"),
                passed > 0 ? "planifié et observé" : "planifié seulement"));
        }

        var scheduleDay = CultureInfo.GetCultureInfo("fr-CA").DateTimeFormat.GetDayName(DateTime.Today.DayOfWeek);
        scheduleDay = CultureInfo.GetCultureInfo("fr-CA").TextInfo.ToTitleCase(scheduleDay);

        return new LiveRouteClientsResponse(
            routeId,
            scheduleDay,
            clients.Count,
            clients.Count(x => x.ParcelsPassed > 0),
            clients.Sum(x => x.ParcelsPassed),
            clients,
            [
                $"Horaire officiel du {scheduleDay.ToLowerInvariant()} : customer_schedule_pickup sur le serveur 101.",
                "Vérification opérationnelle : colis uniques observés sur les convoyeurs haut et sol ou aux postes manuels de Saint-Hubert depuis 16:00."
            ],
            DateTimeOffset.Now);
    }

    public async Task<UnprocessedParcelsResponse> GetUnprocessedParcelsAsync()
    {
        const string sql = """
            WITH scheduled_today AS (
                SELECT p.CUSTOMER_ID customer_id,
                       GROUP_CONCAT(DISTINCT p.ROUTE_ID ORDER BY p.ROUTE_ID SEPARATOR ' / ') routes,
                       COALESCE(GROUP_CONCAT(DISTINCT TIME_FORMAT(p.END_TIME,'%H:%i') ORDER BY p.END_TIME SEPARATOR ' / '),'N/D') pickup_time
                FROM customer_schedule_pickup p
                WHERE p.ROUTE_ID BETWEEN 50000 AND 50099
                  AND CASE DAYOFWEEK(CURDATE())
                        WHEN 1 THEN p.SUNDAY WHEN 2 THEN p.MONDAY WHEN 3 THEN p.TUESDAY
                        WHEN 4 THEN p.WEDNESDAY WHEN 5 THEN p.THURSDAY WHEN 6 THEN p.FRIDAY
                        WHEN 7 THEN p.SATURDAY END = 1
                GROUP BY p.CUSTOMER_ID
            ),
            created_parcels AS (
                SELECT p.PARCEL_ID parcel_id,
                       MAX(p.CUSTOMER_ID) customer_id,
                       MIN(p.INSERT_DATE) created_at
                FROM parcel p
                JOIN scheduled_today st ON st.customer_id=p.CUSTOMER_ID
                WHERE p.INSERT_DATE>=CURDATE()-INTERVAL 2 DAY
                  AND p.INSERT_DATE<CURDATE()+INTERVAL 1 DAY
                  AND p.PARCEL_ID IS NOT NULL
                  AND p.PARCEL_ID<>0
                GROUP BY p.PARCEL_ID
            ),
            passed_parcels AS (
                SELECT DISTINCT ph.PARCEL_ID parcel_id
                FROM parcel_history ph
                JOIN created_parcels cp ON cp.parcel_id=ph.PARCEL_ID
                WHERE ph.EXCEPTION=903
                  AND ph.DEPOT_ID=1
                  AND COALESCE(ph.VOID,0)=0
                  AND ((ph.SOURCE_TYPE=200 AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID IN (1,3))) OR ph.SOURCE_TYPE=201)
            )
            SELECT cp.customer_id,
                   COALESCE(c.NAME,CONCAT('Client ',cp.customer_id)) customer_name,
                   st.routes,
                   st.pickup_time,
                   COUNT(*) unprocessed_parcels,
                   SUM(DATE(cp.created_at)=CURDATE()) created_today,
                   SUM(DATE(cp.created_at)=CURDATE()-INTERVAL 1 DAY) created_yesterday,
                   SUM(DATE(cp.created_at)=CURDATE()-INTERVAL 2 DAY) created_two_days_ago,
                   MIN(cp.created_at) oldest_created,
                   MAX(cp.created_at) newest_created
            FROM created_parcels cp
            JOIN scheduled_today st ON st.customer_id=cp.customer_id
            LEFT JOIN passed_parcels pp ON pp.parcel_id=cp.parcel_id
            LEFT JOIN customer c ON c.CUSTOMER_ID=cp.customer_id
            WHERE pp.parcel_id IS NULL
            GROUP BY cp.customer_id,c.NAME,st.routes,st.pickup_time
            ORDER BY unprocessed_parcels DESC,st.pickup_time,customer_name
            """;

        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<UnprocessedClientRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new UnprocessedClientRow(
                reader.GetInt32("customer_id"),
                reader.GetString("customer_name").Trim(),
                reader.GetString("routes"),
                reader.GetString("pickup_time"),
                Int64OrZero(reader, "created_today"),
                Int64OrZero(reader, "created_yesterday"),
                Int64OrZero(reader, "created_two_days_ago"),
                Int64OrZero(reader, "unprocessed_parcels"),
                reader.GetDateTime("oldest_created"),
                reader.GetDateTime("newest_created")));
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var scheduleDay = CultureInfo.GetCultureInfo("fr-CA").DateTimeFormat.GetDayName(DateTime.Today.DayOfWeek);
        scheduleDay = CultureInfo.GetCultureInfo("fr-CA").TextInfo.ToTitleCase(scheduleDay);
        return new UnprocessedParcelsResponse(
            today.AddDays(-2),
            today,
            scheduleDay,
            rows.Count,
            rows.Sum(row => row.UnprocessedParcels),
            rows,
            [
                "Population : colis uniques créés aujourd'hui, hier ou avant-hier pour les clients d'une route 500xx planifiée aujourd'hui.",
                "Un colis est retiré de la liste dès qu'il possède un passage valide au convoyeur haut, au convoyeur du sol ou au scan manuel de Saint-Hubert."
            ],
            DateTimeOffset.Now);
    }

    public async Task<IReadOnlyList<ScanDepotOption>> GetScanDepotsAsync()
    {
        const string sql = """
            SELECT DEPOTNUMBER,DEPOTNAME,DEPOT_SHORT_LABEL,HAS_CONVEYOR
            FROM depot
            WHERE DEPOTNUMBER>0
              AND DASHBOARD_ACTIVE=1
              AND NULLIF(TRIM(DEPOTNAME),'') IS NOT NULL
            ORDER BY COALESCE(DASHBOARD_ORDER,9999),DEPOTNAME
            """;
        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 30 };
        await using var reader = await command.ExecuteReaderAsync();
        var depots = new List<ScanDepotOption>();
        while (await reader.ReadAsync())
        {
            depots.Add(new ScanDepotOption(
                reader.GetInt32("DEPOTNUMBER"),
                reader.GetString("DEPOTNAME").Trim(),
                IsNull(reader, "DEPOT_SHORT_LABEL") ? string.Empty : reader.GetString("DEPOT_SHORT_LABEL").Trim(),
                !IsNull(reader, "HAS_CONVEYOR") && reader.GetBoolean("HAS_CONVEYOR")));
        }
        return depots;
    }

    public async Task<QuebecDepotScansResponse> GetQuebecDepotScansAsync(int sourceDepotId, DateTime windowStart, DateTime windowEnd)
    {
        const string sql = """
            WITH RECURSIVE
            hours AS (
                SELECT @windowStart bucket_start
                UNION ALL
                SELECT bucket_start+INTERVAL 1 HOUR
                FROM hours
                WHERE bucket_start+INTERVAL 1 HOUR<@windowEnd
            ),
            attribution_anchor AS (
                SELECT @attributionStart code25_start,@attributionEnd code25_end
            ),
            first_by_code AS (
                SELECT ph.EXCEPTION exception_code,ph.PARCEL_ID parcel_id,MIN(ph.DATE_LIV) first_scan
                FROM parcel_history PARTITION (p2026) ph
                WHERE ph.DEPOT_ID=@sourceDepotId
                  AND ph.EXCEPTION IN (903,904,25)
                  AND COALESCE(ph.VOID,0)=0
                  AND ph.PARCEL_ID IS NOT NULL
                  AND ph.PARCEL_ID<>0
                  AND ph.DATE_INSERT>=@insertStart
                  AND ph.DATE_INSERT<@insertEnd
                  AND ph.DATE_LIV>=@windowStart
                  AND ph.DATE_LIV<@windowEnd
                GROUP BY ph.EXCEPTION,ph.PARCEL_ID
            ),
            code25_since_anchor AS (
                SELECT ph.PARCEL_HISTORY_ID code25_history_id,
                       ph.PARCEL_ID parcel_id,
                       ph.DEPOT_ID code25_depot_id,
                       ph.DATE_LIV code25_time
                FROM parcel_history PARTITION (p2026) ph
                CROSS JOIN attribution_anchor a
                WHERE ph.EXCEPTION=25
                  AND COALESCE(ph.VOID,0)=0
                  AND ph.PARCEL_ID IS NOT NULL
                  AND ph.PARCEL_ID<>0
                  AND ph.DEPOT_ID IS NOT NULL
                  AND ph.DEPOT_ID<>0
                  AND ph.DATE_INSERT>=a.code25_start-INTERVAL 1 DAY
                  AND ph.DATE_INSERT<a.code25_end+INTERVAL 1 DAY
                  AND ph.DATE_LIV>=a.code25_start
                  AND ph.DATE_LIV<a.code25_end
            ),
            last_conveyor_ranked AS (
                SELECT c.parcel_id,c.code25_history_id,c.code25_depot_id,
                       conveyor.DEPOT_ID conveyor_depot_id,
                       ROW_NUMBER() OVER(
                           PARTITION BY c.code25_history_id
                           ORDER BY conveyor.DATE_LIV DESC,conveyor.PARCEL_HISTORY_ID DESC
                       ) rn
                FROM code25_since_anchor c
                JOIN parcel_history PARTITION (p2026) conveyor
                  ON conveyor.PARCEL_ID=c.parcel_id
                 AND conveyor.EXCEPTION=903
                 AND COALESCE(conveyor.VOID,0)=0
                 AND (conveyor.DATE_LIV<c.code25_time OR
                      (conveyor.DATE_LIV=c.code25_time AND conveyor.PARCEL_HISTORY_ID<c.code25_history_id))
            ),
            rerouted_25 AS (
                SELECT COUNT(DISTINCT parcel_id) total_code25_rerouted_elsewhere
                FROM last_conveyor_ranked
                WHERE rn=1
                  AND conveyor_depot_id=@sourceDepotId
                  AND code25_depot_id<>conveyor_depot_id
            ),
            hourly AS (
                SELECT @windowStart+INTERVAL TIMESTAMPDIFF(HOUR,@windowStart,first_scan) HOUR bucket_start,
                       SUM(exception_code=903) conveyor_903,
                       SUM(exception_code=904) floor_904,
                       SUM(exception_code=25) code_25
                FROM first_by_code
                GROUP BY bucket_start
            ),
            summary AS (
                SELECT SUM(exception_code=903) total_conveyor_903,
                       SUM(exception_code=904) total_floor_904,
                       SUM(exception_code=25) total_code_25,
                       MAX(first_scan) latest_scan
                FROM first_by_code
            )
            SELECT NOW() database_now,@windowStart day_start,@windowEnd day_end,a.code25_start code25_attribution_since,
                   HOUR(h.bucket_start) hour_value,h.bucket_start,COALESCE(x.conveyor_903,0) conveyor_903,
                   COALESCE(x.floor_904,0) floor_904,COALESCE(x.code_25,0) code_25,
                   COALESCE(s.total_conveyor_903,0) total_conveyor_903,
                   COALESCE(s.total_floor_904,0) total_floor_904,
                   COALESCE(s.total_code_25,0) total_code_25,
                   COALESCE(r25.total_code25_rerouted_elsewhere,0) total_code25_rerouted_elsewhere,
                   s.latest_scan
            FROM hours h
            LEFT JOIN hourly x ON x.bucket_start=h.bucket_start
            CROSS JOIN summary s
            CROSS JOIN rerouted_25 r25
            CROSS JOIN attribution_anchor a
            ORDER BY h.bucket_start
            """;

        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 120 };
        command.Parameters.AddWithValue("@sourceDepotId", sourceDepotId);
        command.Parameters.AddWithValue("@windowStart", windowStart);
        command.Parameters.AddWithValue("@windowEnd", windowEnd);
        command.Parameters.AddWithValue("@insertStart", windowStart.AddDays(-1));
        command.Parameters.AddWithValue("@insertEnd", windowEnd.AddDays(1));
        var attributionStart = windowStart.Date.AddDays(-1).AddHours(22);
        var attributionEnd = windowStart.Date.AddDays(1);
        if (DateTime.Now < attributionEnd) attributionEnd = DateTime.Now;
        command.Parameters.AddWithValue("@attributionStart", attributionStart);
        command.Parameters.AddWithValue("@attributionEnd", attributionEnd);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<QuebecScanHourlyRow>(24);
        var databaseNow = DateTime.Now;
        var dayStart = DateTime.Today;
        var dayEnd = dayStart.AddDays(1);
        var code25AttributionSince = DateTime.Today.AddDays(-1).AddHours(22);
        DateTime? latestScan = null;
        long totalConveyor903 = 0, totalFloor904 = 0, totalCode25 = 0, totalCode25ReroutedElsewhere = 0;
        while (await reader.ReadAsync())
        {
            databaseNow = reader.GetDateTime("database_now");
            dayStart = reader.GetDateTime("day_start");
            dayEnd = reader.GetDateTime("day_end");
            code25AttributionSince = reader.GetDateTime("code25_attribution_since");
            latestScan = IsNull(reader, "latest_scan") ? null : reader.GetDateTime("latest_scan");
            totalConveyor903 = Int64OrZero(reader, "total_conveyor_903");
            totalFloor904 = Int64OrZero(reader, "total_floor_904");
            totalCode25 = Int64OrZero(reader, "total_code_25");
            totalCode25ReroutedElsewhere = Int64OrZero(reader, "total_code25_rerouted_elsewhere");
            rows.Add(new QuebecScanHourlyRow(
                reader.GetInt32("hour_value"),
                reader.GetDateTime("bucket_start"),
                Int64OrZero(reader, "conveyor_903"),
                Int64OrZero(reader, "floor_904"),
                Int64OrZero(reader, "code_25")));
        }

        return new QuebecDepotScansResponse(
            DateOnly.FromDateTime(dayStart), databaseNow, dayStart, dayEnd, latestScan,
            totalConveyor903, totalFloor904, totalCode25, totalCode25ReroutedElsewhere, code25AttributionSince, rows,
            [
                $"Fenêtre opérationnelle enregistrée pour le dépôt {sourceDepotId} : {windowStart:yyyy-MM-dd HH:mm} à {windowEnd.AddMinutes(-1):yyyy-MM-dd HH:mm}.",
                "Les exceptions 903, 904 et 25 incluent tous les SOURCE_TYPE et SOURCE_ID.",
                "Le compteur d'attribution examine les codes 25 de tous les dépôts depuis 22 h la veille et les rattache au dépôt du dernier scan convoyeur 903 antérieur lorsque les deux dépôts diffèrent.",
                "Chaque colis est compté une fois par code, dans l'heure de son premier événement valide de la journée.",
                "Les trois couleurs ne forment pas un total de colis uniques : un colis peut avoir un scan convoyeur 903, un scan 904 et un code 25."
            ],
            DateTimeOffset.Now);
    }

    public async Task<QuebecCode25DestinationsResponse> GetQuebecCode25DestinationsAsync(int sourceDepotId, DateTime windowStart, DateTime windowEnd)
    {
        const string sql = """
            WITH code25_parcels AS (
                SELECT ph.PARCEL_ID,MAX(ph.SHIPPING_ID) shipping_id,MAX(ph.EXP_DATE) exp_date,
                       MIN(ph.DATE_LIV) first_scan,MAX(ph.DATE_LIV) last_scan
                FROM parcel_history PARTITION (p2026) ph
                WHERE ph.DEPOT_ID=@sourceDepotId AND ph.EXCEPTION=25
                  AND COALESCE(ph.VOID,0)=0
                  AND ph.PARCEL_ID IS NOT NULL AND ph.PARCEL_ID<>0
                  AND ph.DATE_INSERT>=@insertStart
                  AND ph.DATE_INSERT<@insertEnd
                  AND ph.DATE_LIV>=@windowStart AND ph.DATE_LIV<@windowEnd
                GROUP BY ph.PARCEL_ID
            ),
            parcel_destination AS (
                SELECT cp.PARCEL_ID,cp.first_scan,cp.last_scan,
                       MAX(s.DEST_ROUTE_ID) destination_route_id,
                       MAX(COALESCE(NULLIF(r.END_DEPOT_ID,0),si.DEPOTNUMBER)) destination_depot_id,
                       COUNT(DISTINCT s.DEST_ROUTE_ID) destination_matches
                FROM code25_parcels cp
                LEFT JOIN shipment s ON s.SHIPPING_ID=cp.shipping_id AND s.EXP_DATE=cp.exp_date
                LEFT JOIN route r ON r.ROUTE_ID=s.DEST_ROUTE_ID
                LEFT JOIN sector_info si ON si.SECTOR_ID=s.DEST_SECTOR_ID
                GROUP BY cp.PARCEL_ID,cp.first_scan,cp.last_scan
            ),
            destination_counts AS (
                SELECT pd.destination_depot_id,
                       COALESCE(NULLIF(TRIM(d.DEPOTNAME),''),d.DEPOT_SHORT_LABEL,d.DEPOTNAMESHORT,
                                CONCAT('Dépôt ',pd.destination_depot_id),'Non déterminé') destination_name,
                       COUNT(DISTINCT pd.destination_route_id) route_count,
                       COUNT(*) parcels,MIN(pd.first_scan) first_scan,MAX(pd.last_scan) last_scan,
                       SUM(pd.destination_matches>1) ambiguous_parcels
                FROM parcel_destination pd
                LEFT JOIN depot d ON d.DEPOTNUMBER=pd.destination_depot_id
                GROUP BY pd.destination_depot_id,d.DEPOT_SHORT_LABEL,d.DEPOTNAMESHORT,d.DEPOTNAME
            )
            SELECT NOW() database_now,dc.*,
                   SUM(dc.parcels) OVER() total_code_25
            FROM destination_counts dc
            ORDER BY dc.parcels DESC,dc.destination_depot_id
            """;

        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 120 };
        command.Parameters.AddWithValue("@sourceDepotId", sourceDepotId);
        command.Parameters.AddWithValue("@windowStart", windowStart);
        command.Parameters.AddWithValue("@windowEnd", windowEnd);
        command.Parameters.AddWithValue("@insertStart", windowStart.AddDays(-1));
        command.Parameters.AddWithValue("@insertEnd", windowEnd.AddDays(1));
        await using var reader = await command.ExecuteReaderAsync();
        var rawRows = new List<(int? DepotId, string DepotName, int RouteCount, long Parcels, DateTime First, DateTime Last)>();
        var databaseNow = DateTime.Now;
        long total = 0;
        while (await reader.ReadAsync())
        {
            databaseNow = reader.GetDateTime("database_now");
            total = Int64OrZero(reader, "total_code_25");
            rawRows.Add((
                NullableInt32(reader, "destination_depot_id"),
                reader.GetString("destination_name").Trim(),
                reader.GetInt32("route_count"),
                Int64OrZero(reader, "parcels"),
                reader.GetDateTime("first_scan"),
                reader.GetDateTime("last_scan")));
        }
        var rows = rawRows.Select(row => new QuebecCode25DestinationRow(
            row.DepotId, row.DepotName, row.RouteCount, row.Parcels,
            total == 0 ? 0 : Math.Round(100m * row.Parcels / total, 1),
            row.First, row.Last)).ToArray();

        return new QuebecCode25DestinationsResponse(
            DateOnly.FromDateTime(windowStart), databaseNow, total, rows,
            [
                "Regroupement par dépôt de destination; toutes les routes du même dépôt sont additionnées.",
                "Le dépôt est obtenu par shipment.DEST_ROUTE_ID, route.END_DEPOT_ID et depot.DEPOTNUMBER.",
                "Lorsque END_DEPOT_ID vaut 0, le dépôt du secteur de destination est utilisé comme repli.",
                "Tous les SOURCE_TYPE et SOURCE_ID sont inclus.",
                $"Chaque colis avec un code 25 valide au dépôt analysé ({sourceDepotId}) est compté une seule fois."
            ],
            DateTimeOffset.Now);
    }

    public async Task<AttributedCode25ParcelsResponse> GetAttributedCode25ParcelsAsync(int attributionDepotId, DateOnly analysisDate)
    {
        const string sql = """
            WITH
            attribution_anchor AS (
                SELECT @attributionStart code25_start,@attributionEnd code25_end
            ),
            code25_since_anchor AS (
                SELECT ph.PARCEL_HISTORY_ID code25_history_id,
                       ph.PARCEL_ID parcel_id,
                       ph.DEPOT_ID code25_depot_id,
                       ph.DATE_LIV code25_time,
                       NULLIF(ph.CUSTOMER_ID,0) history_customer_id,
                       ph.SHIPPING_ID shipping_id,
                       ph.EXP_DATE exp_date
                FROM parcel_history PARTITION (p2026) ph
                CROSS JOIN attribution_anchor a
                WHERE ph.EXCEPTION=25
                  AND COALESCE(ph.VOID,0)=0
                  AND ph.PARCEL_ID IS NOT NULL
                  AND ph.PARCEL_ID<>0
                  AND ph.DEPOT_ID IS NOT NULL
                  AND ph.DEPOT_ID<>0
                  AND ph.DATE_INSERT>=a.code25_start-INTERVAL 1 DAY
                  AND ph.DATE_INSERT<a.code25_end+INTERVAL 1 DAY
                  AND ph.DATE_LIV>=a.code25_start
                  AND ph.DATE_LIV<a.code25_end
            ),
            last_conveyor_ranked AS (
                SELECT c.*,
                       conveyor.DEPOT_ID conveyor_depot_id,
                       conveyor.DATE_LIV conveyor_time,
                       ROW_NUMBER() OVER(
                           PARTITION BY c.code25_history_id
                           ORDER BY conveyor.DATE_LIV DESC,conveyor.PARCEL_HISTORY_ID DESC
                       ) conveyor_rn
                FROM code25_since_anchor c
                JOIN parcel_history PARTITION (p2026) conveyor
                  ON conveyor.PARCEL_ID=c.parcel_id
                 AND conveyor.EXCEPTION=903
                 AND COALESCE(conveyor.VOID,0)=0
                 AND (conveyor.DATE_LIV<c.code25_time OR
                      (conveyor.DATE_LIV=c.code25_time AND conveyor.PARCEL_HISTORY_ID<c.code25_history_id))
            ),
            qualifying_ranked AS (
                SELECT lcr.*,
                       ROW_NUMBER() OVER(
                           PARTITION BY lcr.parcel_id
                           ORDER BY lcr.code25_time DESC,lcr.code25_history_id DESC
                       ) parcel_rn
                FROM last_conveyor_ranked lcr
                WHERE lcr.conveyor_rn=1
                  AND lcr.conveyor_depot_id=@attributionDepotId
                  AND lcr.code25_depot_id<>lcr.conveyor_depot_id
            ),
            final_parcels AS (
                SELECT qr.*,
                       COALESCE(qr.history_customer_id,(
                           SELECT MAX(NULLIF(s.CUSTOMER_ID,0))
                           FROM shipment s
                           WHERE s.SHIPPING_ID=qr.shipping_id AND s.EXP_DATE=qr.exp_date
                       )) customer_id
                FROM qualifying_ranked qr
                WHERE qr.parcel_rn=1
            )
            SELECT NOW() database_now,a.code25_start,
                   COALESCE(NULLIF(TRIM(ad.DEPOTNAME),''),ad.DEPOT_SHORT_LABEL,ad.DEPOTNAMESHORT,
                            CONCAT('Dépôt ',@attributionDepotId)) attribution_depot_name,
                   fp.parcel_id,fp.customer_id,
                   CASE WHEN fp.parcel_id IS NULL THEN NULL
                        ELSE COALESCE(NULLIF(TRIM(c.NAME),''),CONCAT('Client ',fp.customer_id),'Client non identifié') END customer_name,
                   fp.conveyor_time,fp.code25_time,fp.code25_depot_id,
                   CASE WHEN fp.parcel_id IS NULL THEN NULL
                        ELSE COALESCE(NULLIF(TRIM(cd.DEPOTNAME),''),cd.DEPOT_SHORT_LABEL,cd.DEPOTNAMESHORT,
                                      CONCAT('Dépôt ',fp.code25_depot_id)) END code25_depot_name,
                   COUNT(fp.parcel_id) OVER() total_parcels
            FROM attribution_anchor a
            LEFT JOIN depot ad ON ad.DEPOTNUMBER=@attributionDepotId
            LEFT JOIN final_parcels fp ON TRUE
            LEFT JOIN customer c ON c.CUSTOMER_ID=fp.customer_id
            LEFT JOIN depot cd ON cd.DEPOTNUMBER=fp.code25_depot_id
            ORDER BY fp.code25_time DESC,fp.parcel_id
            """;

        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 120 };
        command.Parameters.AddWithValue("@attributionDepotId", attributionDepotId);
        var attributionStart = analysisDate.AddDays(-1).ToDateTime(new TimeOnly(22, 0));
        var attributionEnd = analysisDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        if (DateTime.Now < attributionEnd) attributionEnd = DateTime.Now;
        command.Parameters.AddWithValue("@attributionStart", attributionStart);
        command.Parameters.AddWithValue("@attributionEnd", attributionEnd);
        await using var reader = await command.ExecuteReaderAsync();
        var parcels = new List<AttributedCode25ParcelRow>();
        var databaseNow = DateTime.Now;
        var since = attributionStart;
        var depotName = $"Dépôt {attributionDepotId}";
        long total = 0;
        while (await reader.ReadAsync())
        {
            databaseNow = reader.GetDateTime("database_now");
            since = reader.GetDateTime("code25_start");
            depotName = reader.GetString("attribution_depot_name").Trim();
            total = Int64OrZero(reader, "total_parcels");
            if (IsNull(reader, "parcel_id")) continue;
            parcels.Add(new AttributedCode25ParcelRow(
                reader.GetInt64("parcel_id"),
                NullableInt32(reader, "customer_id"),
                reader.GetString("customer_name").Trim(),
                reader.GetDateTime("conveyor_time"),
                reader.GetDateTime("code25_time"),
                reader.GetInt32("code25_depot_id"),
                reader.GetString("code25_depot_name").Trim()));
        }

        return new AttributedCode25ParcelsResponse(
            databaseNow, since, attributionDepotId, depotName, total, parcels, DateTimeOffset.Now);
    }

    public async Task<QuebecCode25ParcelsResponse> GetQuebecCode25ParcelsAsync(int sourceDepotId, int depotId, DateTime windowStart, DateTime windowEnd)
    {
        const string sql = """
            WITH code25_parcels AS (
                SELECT ph.PARCEL_ID,MAX(ph.SHIPPING_ID) shipping_id,MAX(ph.EXP_DATE) exp_date,
                       MAX(NULLIF(ph.CUSTOMER_ID,0)) history_customer_id,
                       MIN(ph.DATE_LIV) first_scan
                FROM parcel_history PARTITION (p2026) ph
                WHERE ph.DEPOT_ID=@sourceDepotId AND ph.EXCEPTION=25
                  AND COALESCE(ph.VOID,0)=0
                  AND ph.PARCEL_ID IS NOT NULL AND ph.PARCEL_ID<>0
                  AND ph.DATE_INSERT>=@insertStart
                  AND ph.DATE_INSERT<@insertEnd
                  AND ph.DATE_LIV>=@windowStart AND ph.DATE_LIV<@windowEnd
                GROUP BY ph.PARCEL_ID
            ),
            parcel_destination AS (
                SELECT cp.PARCEL_ID,cp.first_scan,
                       MAX(s.DEST_ROUTE_ID) destination_route_id,
                       MAX(s.DEST_SECTOR_ID) destination_sector_id,
                       MAX(COALESCE(NULLIF(r.END_DEPOT_ID,0),si.DEPOTNUMBER)) destination_depot_id,
                       MAX(COALESCE(cp.history_customer_id,NULLIF(s.CUSTOMER_ID,0))) customer_id
                FROM code25_parcels cp
                LEFT JOIN shipment s ON s.SHIPPING_ID=cp.shipping_id AND s.EXP_DATE=cp.exp_date
                LEFT JOIN route r ON r.ROUTE_ID=s.DEST_ROUTE_ID
                LEFT JOIN sector_info si ON si.SECTOR_ID=s.DEST_SECTOR_ID
                GROUP BY cp.PARCEL_ID,cp.first_scan
            ),
            first_qc_ranked AS (
                SELECT ph.PARCEL_ID,ph.PARCEL_HISTORY_ID,ph.DATE_LIV,
                       ROW_NUMBER() OVER(PARTITION BY ph.PARCEL_ID ORDER BY ph.DATE_LIV,ph.PARCEL_HISTORY_ID) rn
                FROM parcel_history PARTITION (p2026) ph
                JOIN code25_parcels cp ON cp.PARCEL_ID=ph.PARCEL_ID
                WHERE ph.DEPOT_ID=@sourceDepotId AND COALESCE(ph.VOID,0)=0
            ),
            first_qc AS (
                SELECT PARCEL_ID,PARCEL_HISTORY_ID,DATE_LIV
                FROM first_qc_ranked
                WHERE rn=1
            ),
            prior_ranked AS (
                SELECT ph.PARCEL_ID,ph.DATE_LIV,ph.EXCEPTION,ph.WEIGHT,ph.LENGTH,ph.WIDTH,ph.HEIGHT,
                       ROW_NUMBER() OVER(
                           PARTITION BY ph.PARCEL_ID
                           ORDER BY CASE ph.EXCEPTION WHEN 903 THEN 0 ELSE 1 END,
                                    ph.DATE_LIV DESC,ph.PARCEL_HISTORY_ID DESC
                       ) rn
                FROM parcel_history PARTITION (p2026) ph
                JOIN first_qc q ON q.PARCEL_ID=ph.PARCEL_ID
                  AND (ph.DATE_LIV<q.DATE_LIV OR (ph.DATE_LIV=q.DATE_LIV AND ph.PARCEL_HISTORY_ID<q.PARCEL_HISTORY_ID))
                WHERE COALESCE(ph.VOID,0)=0 AND ph.EXCEPTION IN (903,901)
            ),
            prior_scan AS (
                SELECT PARCEL_ID,DATE_LIV,EXCEPTION,WEIGHT,LENGTH,WIDTH,HEIGHT
                FROM prior_ranked
                WHERE rn=1
            )
            SELECT NOW() database_now,pd.PARCEL_ID,pd.customer_id,
                   COALESCE(NULLIF(TRIM(c.NAME),''),CONCAT('Client ',pd.customer_id),'Client non identifié') customer_name,
                   pd.destination_sector_id,pd.first_scan,
                   CASE WHEN ps.WEIGHT>0 THEN ps.WEIGHT END previous_weight,
                   CASE WHEN ps.LENGTH>0 THEN ps.LENGTH END previous_length,
                   CASE WHEN ps.WIDTH>0 THEN ps.WIDTH END previous_width,
                   CASE WHEN ps.HEIGHT>0 THEN ps.HEIGHT END previous_height,
                   ps.DATE_LIV previous_scan_date,
                   ps.EXCEPTION previous_scan_code,
                   COALESCE(NULLIF(TRIM(d.DEPOTNAME),''),d.DEPOT_SHORT_LABEL,d.DEPOTNAMESHORT,
                            CONCAT('Dépôt ',@depotId)) destination_name,
                   COUNT(*) OVER() total_parcels
            FROM parcel_destination pd
            LEFT JOIN customer c ON c.CUSTOMER_ID=pd.customer_id
            LEFT JOIN depot d ON d.DEPOTNUMBER=pd.destination_depot_id
            LEFT JOIN prior_scan ps ON ps.PARCEL_ID=pd.PARCEL_ID
            WHERE pd.destination_depot_id=@depotId
            ORDER BY pd.first_scan,pd.PARCEL_ID
            """;

        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 120 };
        command.Parameters.AddWithValue("@sourceDepotId", sourceDepotId);
        command.Parameters.AddWithValue("@depotId", depotId);
        command.Parameters.AddWithValue("@windowStart", windowStart);
        command.Parameters.AddWithValue("@windowEnd", windowEnd);
        command.Parameters.AddWithValue("@insertStart", windowStart.AddDays(-1));
        command.Parameters.AddWithValue("@insertEnd", windowEnd.AddDays(1));
        await using var reader = await command.ExecuteReaderAsync();
        var parcels = new List<QuebecCode25ParcelRow>();
        var databaseNow = DateTime.Now;
        var depotName = $"Dépôt {depotId}";
        long total = 0;
        while (await reader.ReadAsync())
        {
            databaseNow = reader.GetDateTime("database_now");
            depotName = reader.GetString("destination_name").Trim();
            total = Int64OrZero(reader, "total_parcels");
            parcels.Add(new QuebecCode25ParcelRow(
                reader.GetInt64("PARCEL_ID"),
                NullableInt32(reader, "customer_id"),
                reader.GetString("customer_name").Trim(),
                NullableInt32(reader, "destination_sector_id"),
                NullableDecimal(reader, "previous_weight"),
                NullableDecimal(reader, "previous_length"),
                NullableDecimal(reader, "previous_width"),
                NullableDecimal(reader, "previous_height"),
                IsNull(reader, "previous_scan_date") ? null : reader.GetDateTime("previous_scan_date"),
                NullableInt32(reader, "previous_scan_code"),
                reader.GetDateTime("first_scan")));
        }

        return new QuebecCode25ParcelsResponse(
            DateOnly.FromDateTime(windowStart), databaseNow, depotId, depotName, total, parcels, DateTimeOffset.Now);
    }

    public async Task<ParcelHistoryResponse> GetParcelHistoryAsync(long parcelId)
    {
        const string sql = """
            WITH shipment_address_ranked AS (
                SELECT NULLIF(TRIM(CONCAT_WS(' ',s.DEST_ADDRESS1,s.DEST_ADDRESS2)),'') destination_address,
                       NULLIF(TRIM(s.DEST_CITY),'') destination_city,
                       ROW_NUMBER() OVER(ORDER BY ph.DATE_LIV DESC,ph.PARCEL_HISTORY_ID DESC) rn
                FROM parcel_history ph
                JOIN shipment s ON s.SHIPPING_ID=ph.SHIPPING_ID AND s.EXP_DATE=ph.EXP_DATE
                WHERE ph.PARCEL_ID=@parcelId
                  AND COALESCE(ph.VOID,0)=0
                  AND ph.SHIPPING_ID IS NOT NULL
            )
            SELECT NOW() database_now,ph.PARCEL_HISTORY_ID,ph.EXCEPTION exception_code,
                   COALESCE(NULLIF(TRIM(e.TXTFRENCH),''),NULLIF(TRIM(e.EX_NAME_FR),''),CONCAT('Code ',ph.EXCEPTION)) description,
                   NULLIF(TRIM(se.DESCRIPTION_FR),'') sub_description,
                   ph.DATE_LIV event_date,
                   (SELECT destination_address FROM shipment_address_ranked WHERE rn=1) destination_address,
                   (SELECT destination_city FROM shipment_address_ranked WHERE rn=1) destination_city,
                   COALESCE(
                       NULLIF(TRIM(CONCAT_WS(' ',nu.FIRSTNAME,nu.LASTNAME)),''),
                       CASE WHEN ph.EXCEPTION=903 AND ph.CHUTE_NO IS NOT NULL
                            THEN CONCAT('CONVOYEUR CHUTE ',ph.CHUTE_NO) END,
                       CASE WHEN ph.TPSL IS NOT NULL AND ph.TPSL<>0
                            THEN CONCAT('TPSL:',ph.TPSL,
                                CASE WHEN NULLIF(TRIM(COALESCE(tr.ROUTE_DRIVER_NAME,tr.USERNAME,tr.ROUTE_NAME)),'') IS NULL THEN ''
                                     ELSE CONCAT(' ',TRIM(COALESCE(tr.ROUTE_DRIVER_NAME,tr.USERNAME,tr.ROUTE_NAME))) END)
                            END,
                       NULLIF(TRIM(psi.SOURCE_DESCRIPTION),''),
                       NULLIF(TRIM(ph.CONTACT),''),
                       '—'
                   ) user_or_tpsl,
                   ph.DEPOT_ID depot_id,
                   COALESCE(NULLIF(TRIM(d.DEPOTNAME),''),
                            CASE WHEN ph.DEPOT_ID IS NULL OR ph.DEPOT_ID=0 THEN '—'
                                 ELSE CONCAT('Dépôt ',ph.DEPOT_ID) END) depot_name
            FROM parcel_history ph
            LEFT JOIN exceptions e ON e.CODE=ph.EXCEPTION
            LEFT JOIN sub_exceptions se ON se.SUB_EXCEPTION_ID=ph.SUB_EXCEPTION_ID
            LEFT JOIN nat_user nu ON nu.NAT_USER_ID=ph.USER_ID
            LEFT JOIN route tr ON tr.ROUTE_ID=ph.TPSL
            LEFT JOIN parcel_history_source_id psi ON psi.SOURCE_ID=ph.SOURCE_ID
            LEFT JOIN depot d ON d.DEPOTNUMBER=ph.DEPOT_ID
            WHERE ph.PARCEL_ID=@parcelId AND COALESCE(ph.VOID,0)=0
            ORDER BY ph.DATE_LIV DESC,ph.PARCEL_HISTORY_ID DESC
            """;

        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 120 };
        command.Parameters.AddWithValue("@parcelId", parcelId);
        await using var reader = await command.ExecuteReaderAsync();
        var events = new List<ParcelHistoryEventRow>();
        var databaseNow = DateTime.Now;
        var destinationAddress = string.Empty;
        var destinationCity = string.Empty;
        while (await reader.ReadAsync())
        {
            databaseNow = reader.GetDateTime("database_now");
            destinationAddress = IsNull(reader, "destination_address") ? string.Empty : reader.GetString("destination_address").Trim();
            destinationCity = IsNull(reader, "destination_city") ? string.Empty : reader.GetString("destination_city").Trim();
            var description = reader.GetString("description").Trim();
            if (!IsNull(reader, "sub_description"))
            {
                var subDescription = reader.GetString("sub_description").Trim();
                if (subDescription.Length > 0) description += $" — {subDescription}";
            }
            events.Add(new ParcelHistoryEventRow(
                reader.GetInt64("PARCEL_HISTORY_ID"),
                reader.GetInt32("exception_code"),
                description,
                reader.GetDateTime("event_date"),
                reader.GetString("user_or_tpsl").Trim(),
                NullableInt32(reader, "depot_id"),
                reader.GetString("depot_name").Trim()));
        }

        return new ParcelHistoryResponse(parcelId, databaseNow, destinationAddress, destinationCity, events, DateTimeOffset.Now);
    }

    public async Task<ConveyorHourlyResponse> GetConveyorHourlyAsync(DateOnly date)
    {
        const string sql = """
            WITH RECURSIVE
            shift_anchor AS (
                SELECT @shiftStart shift_start
            ),
            shift_bounds AS (
                SELECT shift_start,shift_start+INTERVAL 12 HOUR shift_end
                FROM shift_anchor
            ),
            hour_slots AS (
                SELECT 0 slot_index,16 hour_value
                UNION ALL
                SELECT slot_index+1,MOD(hour_value+1,24) FROM hour_slots WHERE slot_index<11
            ),
            sources AS (
                SELECT 'high' source_key
                UNION ALL SELECT 'floor'
                UNION ALL SELECT 'manual'
            ),
            classified_scans AS (
                SELECT ph.PARCEL_ID parcel_id,
                       CASE
                           WHEN ph.SOURCE_TYPE=200 AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID=1) THEN 'high'
                           WHEN ph.SOURCE_TYPE=200 AND ph.SOURCE_ID=3 THEN 'floor'
                           WHEN ph.SOURCE_TYPE=201 THEN 'manual'
                       END source_key,
                       ph.DATE_LIV scan_time
                FROM parcel_history ph
                CROSS JOIN shift_bounds sb
                WHERE ph.EXCEPTION=903
                  AND ph.DEPOT_ID=1
                  AND COALESCE(ph.VOID,0)=0
                  AND ph.PARCEL_ID IS NOT NULL
                  AND ph.PARCEL_ID<>0
                  AND ((ph.SOURCE_TYPE=200 AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID IN (1,3))) OR ph.SOURCE_TYPE=201)
                  AND ph.DATE_INSERT>=sb.shift_start-INTERVAL 1 HOUR
                  AND ph.DATE_INSERT<sb.shift_end
                  AND ph.DATE_LIV>=sb.shift_start
                  AND ph.DATE_LIV<sb.shift_end
            ),
            first_by_source AS (
                SELECT source_key,parcel_id,MIN(scan_time) first_scan
                FROM classified_scans
                GROUP BY source_key,parcel_id
            ),
            hourly_counts AS (
                SELECT source_key,HOUR(first_scan) hour_value,COUNT(*) parcels
                FROM first_by_source
                GROUP BY source_key,HOUR(first_scan)
            ),
            source_summary AS (
                SELECT source_key,COUNT(*) total_parcels,MIN(first_scan) first_scan,MAX(first_scan) last_scan
                FROM first_by_source
                GROUP BY source_key
            )
            SELECT NOW() database_now,DATE(sb.shift_start) shift_date,sb.shift_start,sb.shift_end,
                   s.source_key,h.slot_index,h.hour_value,COALESCE(hc.parcels,0) parcels,
                   COALESCE(ss.total_parcels,0) total_parcels,ss.first_scan,ss.last_scan
            FROM sources s
            CROSS JOIN shift_bounds sb
            CROSS JOIN hour_slots h
            LEFT JOIN hourly_counts hc ON hc.source_key=s.source_key AND hc.hour_value=h.hour_value
            LEFT JOIN source_summary ss ON ss.source_key=s.source_key
            ORDER BY FIELD(s.source_key,'high','floor','manual'),h.slot_index
            """;

        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 90 };
        command.Parameters.AddWithValue("@shiftStart", date.ToDateTime(new TimeOnly(16, 0)));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<ConveyorHourlyRow>();
        var databaseNow = DateTime.Now;
        var shiftStart = DateTime.Today.AddHours(16);
        var shiftEnd = shiftStart.AddHours(12);
        long totalHigh = 0, totalFloor = 0, totalManual = 0;
        DateTime? firstHigh = null, firstFloor = null, firstManual = null;
        DateTime? lastHigh = null, lastFloor = null, lastManual = null;
        while (await reader.ReadAsync())
        {
            databaseNow = reader.GetDateTime("database_now");
            shiftStart = reader.GetDateTime("shift_start");
            shiftEnd = reader.GetDateTime("shift_end");
            var source = reader.GetString("source_key");
            var sourceTotal = Int64OrZero(reader, "total_parcels");
            DateTime? firstScan = IsNull(reader, "first_scan") ? null : reader.GetDateTime("first_scan");
            DateTime? lastScan = IsNull(reader, "last_scan") ? null : reader.GetDateTime("last_scan");
            if (source == "high") { totalHigh = sourceTotal; firstHigh = firstScan; lastHigh = lastScan; }
            else if (source == "floor") { totalFloor = sourceTotal; firstFloor = firstScan; lastFloor = lastScan; }
            else { totalManual = sourceTotal; firstManual = firstScan; lastManual = lastScan; }
            rows.Add(new ConveyorHourlyRow(
                source,
                reader.GetInt32("hour_value"),
                Int64OrZero(reader, "parcels")));
        }

        return new ConveyorHourlyResponse(
            DateOnly.FromDateTime(shiftStart),
            databaseNow,
            shiftStart,
            shiftEnd,
            totalHigh,
            totalFloor,
            totalManual,
            firstHigh,
            firstFloor,
            firstManual,
            lastHigh,
            lastFloor,
            lastManual,
            rows,
            [
                "Chaque colis unique est compté dans l'heure de son premier passage sur la source concernée.",
                "Le quart opérationnel commence à 16 h et se termine à 3 h 59 le lendemain; après minuit, les données restent rattachées au quart de la veille."
            ],
            DateTimeOffset.Now);
    }

    public async Task<ConveyorQualityResponse> GetConveyorQualityAsync(DateOnly date)
    {
        const string sql = """
            WITH scope AS (
                SELECT parcel_id,line_id,chute,camera_data
                FROM parcel_scan_history
                WHERE depot_id=1
                  AND line_id IN (0,1,3)
                  AND date_insert>=@shiftStart
                  AND date_insert<@shiftEnd
            ),
            same_chute_repeat AS (
                SELECT parcel_id,line_id,chute
                FROM scope
                WHERE parcel_id IS NOT NULL
                  AND parcel_id<>0
                  AND chute IS NOT NULL
                  AND chute<>98
                GROUP BY parcel_id,line_id,chute
                HAVING COUNT(*)>=2
            ),
            quality_summary AS (
                SELECT COUNT(*) total_conveyed,
                       COALESCE(SUM(chute=98),0) chute_98,
                       COALESCE(SUM((parcel_id IS NULL OR parcel_id=0) AND camera_data LIKE '?%'),0) no_read
                FROM scope
            ),
            top_chutes AS (
                SELECT chute,COUNT(*) recirculated_parcels
                FROM same_chute_repeat
                GROUP BY chute
                ORDER BY recirculated_parcels DESC,chute
                LIMIT 5
            )
            SELECT qs.total_conveyed,qs.chute_98,qs.no_read,
                   (SELECT COUNT(DISTINCT parcel_id) FROM same_chute_repeat) same_chute_recirculated,
                   tc.chute,tc.recirculated_parcels
            FROM quality_summary qs
            LEFT JOIN top_chutes tc ON 1=1
            ORDER BY tc.recirculated_parcels DESC,tc.chute
            """;

        var shiftStart = date.ToDateTime(new TimeOnly(16, 0));
        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 90 };
        command.Parameters.AddWithValue("@shiftStart", shiftStart);
        command.Parameters.AddWithValue("@shiftEnd", shiftStart.AddHours(12));
        await using var reader = await command.ExecuteReaderAsync();
        long totalConveyed = 0, chute98 = 0, noRead = 0, sameChuteRecirculated = 0;
        var topChutes = new List<RecirculationChute>(5);
        while (await reader.ReadAsync())
        {
            totalConveyed = Int64OrZero(reader, "total_conveyed");
            chute98 = Int64OrZero(reader, "chute_98");
            noRead = Int64OrZero(reader, "no_read");
            sameChuteRecirculated = Int64OrZero(reader, "same_chute_recirculated");
            if (!IsNull(reader, "chute"))
                topChutes.Add(new RecirculationChute(reader.GetInt32("chute"), Int64OrZero(reader, "recirculated_parcels")));
        }
        return new ConveyorQualityResponse(
            date,
            totalConveyed,
            chute98,
            noRead,
            sameChuteRecirculated,
            topChutes,
            DateTimeOffset.Now);
    }

    public async Task<HighConveyorCapacityResponse> GetHighConveyorCapacityAsync(DateOnly date)
    {
        var benchmark = await GetHighCapacityBenchmarkAsync();
        const string sql = """
            WITH
            shift_anchor AS (
                SELECT @shiftStart shift_start
            ),
            shift_bounds AS (
                SELECT shift_start,shift_start+INTERVAL 12 HOUR shift_end FROM shift_anchor
            ),
            scans AS (
                SELECT ph.PARCEL_ID parcel_id,ph.DATE_LIV scan_time,ph.CHUTE_NO chute_no
                FROM parcel_history PARTITION (p2026) ph
                CROSS JOIN shift_bounds sb
                WHERE ph.EXCEPTION=903
                  AND ph.DEPOT_ID=1
                  AND ph.SOURCE_TYPE=200
                  AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID=1)
                  AND COALESCE(ph.VOID,0)=0
                  AND ph.PARCEL_ID IS NOT NULL
                  AND ph.PARCEL_ID<>0
                  AND ph.DATE_INSERT>=sb.shift_start-INTERVAL 1 HOUR
                  AND ph.DATE_INSERT<sb.shift_end
                  AND ph.DATE_LIV>=sb.shift_start
                  AND ph.DATE_LIV<sb.shift_end
            ),
            first_parcel AS (
                SELECT parcel_id,MIN(scan_time) first_scan
                FROM scans
                GROUP BY parcel_id
            ),
            unique_minute_counts AS (
                SELECT CAST(DATE_FORMAT(first_scan,'%Y-%m-%d %H:%i:00') AS DATETIME) minute_start,
                       COUNT(*) unique_parcels
                FROM first_parcel
                GROUP BY CAST(DATE_FORMAT(first_scan,'%Y-%m-%d %H:%i:00') AS DATETIME)
            ),
            total_minute_counts AS (
                SELECT CAST(DATE_FORMAT(scan_time,'%Y-%m-%d %H:%i:00') AS DATETIME) minute_start,
                       COUNT(*) total_parcels
                FROM scans
                GROUP BY CAST(DATE_FORMAT(scan_time,'%Y-%m-%d %H:%i:00') AS DATETIME)
            ),
            chute98_bucket_counts AS (
                SELECT TIMESTAMP(DATE(scan_time),MAKETIME(HOUR(scan_time),FLOOR(MINUTE(scan_time)/15)*15,0)) bucket_start,
                       COUNT(DISTINCT parcel_id) chute_98
                FROM scans
                WHERE chute_no=98
                GROUP BY TIMESTAMP(DATE(scan_time),MAKETIME(HOUR(scan_time),FLOOR(MINUTE(scan_time)/15)*15,0))
            ),
            ordered_scans AS (
                SELECT parcel_id,scan_time,
                       ROW_NUMBER() OVER (PARTITION BY parcel_id ORDER BY scan_time) scan_sequence
                FROM scans
            ),
            first_recirculation AS (
                SELECT parcel_id,scan_time recirculation_time
                FROM ordered_scans
                WHERE scan_sequence=2
            ),
            recirculated_bucket_counts AS (
                SELECT TIMESTAMP(DATE(recirculation_time),MAKETIME(HOUR(recirculation_time),FLOOR(MINUTE(recirculation_time)/15)*15,0)) bucket_start,
                       COUNT(*) recirculated
                FROM first_recirculation
                GROUP BY TIMESTAMP(DATE(recirculation_time),MAKETIME(HOUR(recirculation_time),FLOOR(MINUTE(recirculation_time)/15)*15,0))
            ),
            minute_counts AS (
                SELECT minute_start FROM unique_minute_counts
                UNION
                SELECT minute_start FROM total_minute_counts
            )
            SELECT NOW() database_now,sb.shift_start,sb.shift_end,mc.minute_start,
                   COALESCE(uc.unique_parcels,0) unique_parcels,
                   COALESCE(tc.total_parcels,0) total_parcels,
                   TIMESTAMP(DATE(mc.minute_start),MAKETIME(HOUR(mc.minute_start),FLOOR(MINUTE(mc.minute_start)/15)*15,0)) bucket_start,
                   COALESCE(rc.recirculated,0) recirculated,
                   COALESCE(c98.chute_98,0) chute_98
            FROM shift_bounds sb
            LEFT JOIN minute_counts mc ON 1=1
            LEFT JOIN unique_minute_counts uc ON uc.minute_start=mc.minute_start
            LEFT JOIN total_minute_counts tc ON tc.minute_start=mc.minute_start
            LEFT JOIN chute98_bucket_counts c98
              ON c98.bucket_start=TIMESTAMP(DATE(mc.minute_start),MAKETIME(HOUR(mc.minute_start),FLOOR(MINUTE(mc.minute_start)/15)*15,0))
            LEFT JOIN recirculated_bucket_counts rc
              ON rc.bucket_start=TIMESTAMP(DATE(mc.minute_start),MAKETIME(HOUR(mc.minute_start),FLOOR(MINUTE(mc.minute_start)/15)*15,0))
            ORDER BY mc.minute_start
            """;

        await using var connection = await OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 120 };
        command.Parameters.AddWithValue("@shiftStart", date.ToDateTime(new TimeOnly(16, 0)));
        await using var reader = await command.ExecuteReaderAsync();
        var uniqueMinuteCounts = new Dictionary<DateTime, long>();
        var totalMinuteCounts = new Dictionary<DateTime, long>();
        var recirculatedBucketCounts = new Dictionary<DateTime, long>();
        var chute98BucketCounts = new Dictionary<DateTime, long>();
        var databaseNow = DateTime.Now;
        var shiftStart = DateTime.Today.AddHours(16);
        var shiftEnd = shiftStart.AddHours(12);
        while (await reader.ReadAsync())
        {
            databaseNow = reader.GetDateTime("database_now");
            shiftStart = reader.GetDateTime("shift_start");
            shiftEnd = reader.GetDateTime("shift_end");
            if (!IsNull(reader, "minute_start"))
            {
                var minuteStart = reader.GetDateTime("minute_start");
                uniqueMinuteCounts[minuteStart] = Int64OrZero(reader, "unique_parcels");
                totalMinuteCounts[minuteStart] = Int64OrZero(reader, "total_parcels");
                recirculatedBucketCounts[reader.GetDateTime("bucket_start")] = Int64OrZero(reader, "recirculated");
                chute98BucketCounts[reader.GetDateTime("bucket_start")] = Int64OrZero(reader, "chute_98");
            }
        }

        var analysisEnd = databaseNow < shiftStart ? shiftStart : databaseNow > shiftEnd ? shiftEnd : databaseNow;
        var buckets = new List<HighCapacityBucket>(48);
        for (var bucketStart = shiftStart; bucketStart < shiftEnd; bucketStart = bucketStart.AddMinutes(15))
        {
            var bucketEnd = bucketStart.AddMinutes(15);
            var isFuture = bucketStart >= analysisEnd;
            var measuredEnd = bucketEnd < analysisEnd ? bucketEnd : analysisEnd;
            var measuredMinutes = isFuture ? 0 : Math.Max(1, (int)Math.Ceiling((measuredEnd - bucketStart).TotalMinutes));
            var uniqueParcels = isFuture ? 0 : uniqueMinuteCounts.Where(x => x.Key >= bucketStart && x.Key < measuredEnd).Sum(x => x.Value);
            var totalParcels = isFuture ? 0 : totalMinuteCounts.Where(x => x.Key >= bucketStart && x.Key < measuredEnd).Sum(x => x.Value);
            var recirculated = isFuture ? 0 : recirculatedBucketCounts.GetValueOrDefault(bucketStart);
            var chute98 = isFuture ? 0 : chute98BucketCounts.GetValueOrDefault(bucketStart);
            var rate = measuredMinutes == 0 ? 0 : Math.Round(60m * uniqueParcels / measuredMinutes, 0);
            var utilization = benchmark.PracticalCapacityPerHour == 0 ? 0 : Math.Round(100m * rate / benchmark.PracticalCapacityPerHour, 1);
            var status = isFuture ? "future" : utilization >= 80 ? "capacity" : utilization >= 40 ? "under" : "gap";
            buckets.Add(new HighCapacityBucket(bucketStart, uniqueParcels, totalParcels, recirculated, chute98, rate, utilization, status, isFuture));
        }

        var firstScan = uniqueMinuteCounts.Count == 0 ? (DateTime?)null : uniqueMinuteCounts.Keys.Min();
        var totalUniqueParcels = uniqueMinuteCounts.Where(x => x.Key < analysisEnd).Sum(x => x.Value);
        var activeEnd = uniqueMinuteCounts.Count == 0
            ? (DateTime?)null
            : uniqueMinuteCounts.Keys.Where(x => x < analysisEnd).DefaultIfEmpty().Max().AddMinutes(1);
        if (activeEnd > analysisEnd) activeEnd = analysisEnd;
        var activeMinutes = firstScan is null || activeEnd is null
            ? 0
            : Math.Max(1, (int)Math.Ceiling((activeEnd.Value - firstScan.Value).TotalMinutes));
        var excludedZeroMinutes = firstScan is null || activeEnd is null
            ? 0
            : 15 * buckets.Count(x =>
                !x.IsFuture
                && x.BucketStart >= firstScan.Value
                && x.BucketStart.AddMinutes(15) <= activeEnd.Value
                && x.TotalParcels == 0);
        var potentialMinutes = Math.Max(0, activeMinutes - excludedZeroMinutes);
        var potentialParcels = benchmark.PracticalCapacityPerHour == 0
            ? 0
            : (long)Math.Round(benchmark.PracticalCapacityPerHour * potentialMinutes / 60m, 0, MidpointRounding.AwayFromZero);
        var elapsedMinutes = firstScan is null ? 0 : Math.Max(1, (int)Math.Ceiling((analysisEnd - firstScan.Value).TotalMinutes));
        var average = elapsedMinutes == 0 ? 0 : Math.Round(60m * totalUniqueParcels / elapsedMinutes, 0);
        var averageUtilization = benchmark.PracticalCapacityPerHour == 0 ? 0 : Math.Round(100m * average / benchmark.PracticalCapacityPerHour, 1);
        var capacityThreshold = (long)Math.Ceiling(benchmark.PracticalCapacityPerHour / 60m * 0.8m);
        var minutesAtCapacity = uniqueMinuteCounts.Count(x => x.Key < analysisEnd && x.Value >= capacityThreshold);
        var gaps = BuildHighCapacityGaps(firstScan, analysisEnd, uniqueMinuteCounts, benchmark.PracticalCapacityPerHour);
        var currentRate = buckets.LastOrDefault(x => !x.IsFuture)?.ParcelsPerHour ?? 0;

        return new HighConveyorCapacityResponse(
            DateOnly.FromDateTime(shiftStart), databaseNow, shiftStart, shiftEnd,
            benchmark.DailyPeaks.Count, benchmark.PracticalCapacityPerHour, benchmark.MaximumObservedPerHour,
            currentRate, average, averageUtilization,
            activeMinutes, excludedZeroMinutes, potentialMinutes, potentialParcels,
            minutesAtCapacity, gaps.Sum(x => x.DurationMinutes), benchmark.DailyPeaks, buckets, gaps,
            [
                "CapacitÃ© pratique : 75e percentile du meilleur volume observÃ© dans une fenÃªtre continue de 60 minutes pour chaque quart complÃ©tÃ© des 14 derniers jours.",
                "Un intervalle est Ã  capacitÃ© Ã  partir de 80 % du benchmark; un creux est une pÃ©riode d'au moins 5 minutes sous 40 %.",
                "Le benchmark dÃ©crit le dÃ©bit pratique observÃ©, pas la capacitÃ© mÃ©canique maximale. Les creux n'en indiquent pas automatiquement la cause."
            ],
            DateTimeOffset.Now);
    }

    private async Task<CapacityBenchmarkSnapshot> GetHighCapacityBenchmarkAsync()
    {
        if (capacityBenchmarkCache is { } cached && DateTimeOffset.Now - cached.LoadedAt < TimeSpan.FromHours(1)) return cached;
        await capacityBenchmarkLock.WaitAsync();
        try
        {
            if (capacityBenchmarkCache is { } refreshed && DateTimeOffset.Now - refreshed.LoadedAt < TimeSpan.FromHours(1)) return refreshed;
            const string sql = """
                WITH
                shift_anchor AS (
                    SELECT CASE
                        WHEN CURTIME()<'04:00:00' THEN CURDATE()-INTERVAL 1 DAY+INTERVAL 16 HOUR
                        ELSE CURDATE()+INTERVAL 16 HOUR
                    END current_shift_start
                ),
                first_by_shift AS (
                    SELECT DATE(ph.DATE_LIV-INTERVAL 4 HOUR) shift_date,
                           ph.PARCEL_ID parcel_id,MIN(ph.DATE_LIV) first_scan
                    FROM parcel_history PARTITION (p2026) ph
                    CROSS JOIN shift_anchor sa
                    WHERE ph.EXCEPTION=903
                      AND ph.DEPOT_ID=1
                      AND ph.SOURCE_TYPE=200
                      AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID=1)
                      AND COALESCE(ph.VOID,0)=0
                      AND ph.PARCEL_ID IS NOT NULL
                      AND ph.PARCEL_ID<>0
                      AND ph.DATE_INSERT>=sa.current_shift_start-INTERVAL 14 DAY-INTERVAL 1 HOUR
                      AND ph.DATE_INSERT<sa.current_shift_start
                      AND ph.DATE_LIV>=sa.current_shift_start-INTERVAL 14 DAY
                      AND ph.DATE_LIV<sa.current_shift_start
                      AND (HOUR(ph.DATE_LIV)>=16 OR HOUR(ph.DATE_LIV)<4)
                    GROUP BY DATE(ph.DATE_LIV-INTERVAL 4 HOUR),ph.PARCEL_ID
                ),
                minute_counts AS (
                    SELECT shift_date,CAST(DATE_FORMAT(first_scan,'%Y-%m-%d %H:%i:00') AS DATETIME) minute_start,COUNT(*) parcels
                    FROM first_by_shift
                    GROUP BY shift_date,CAST(DATE_FORMAT(first_scan,'%Y-%m-%d %H:%i:00') AS DATETIME)
                ),
                rolling_60_minutes AS (
                    SELECT m1.shift_date,m1.minute_start window_start,SUM(m2.parcels) parcels_per_hour
                    FROM minute_counts m1
                    JOIN minute_counts m2
                      ON m2.shift_date=m1.shift_date
                     AND m2.minute_start>=m1.minute_start
                     AND m2.minute_start<m1.minute_start+INTERVAL 60 MINUTE
                    WHERE m1.minute_start<=TIMESTAMP(m1.shift_date)+INTERVAL 27 HOUR
                    GROUP BY m1.shift_date,m1.minute_start
                ),
                ranked_hours AS (
                    SELECT shift_date,window_start,parcels_per_hour,
                           ROW_NUMBER() OVER (PARTITION BY shift_date ORDER BY parcels_per_hour DESC,window_start) peak_rank
                    FROM rolling_60_minutes
                ),
                shift_totals AS (
                    SELECT shift_date,COUNT(*) total_parcels
                    FROM first_by_shift
                    GROUP BY shift_date
                )
                SELECT rh.shift_date,rh.window_start,rh.parcels_per_hour,st.total_parcels
                FROM ranked_hours rh
                JOIN shift_totals st ON st.shift_date=rh.shift_date
                WHERE rh.peak_rank=1
                ORDER BY rh.shift_date DESC
                """;
            await using var connection = await OpenAsync();
            await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 180 };
            await using var reader = await command.ExecuteReaderAsync();
            var peaks = new List<HighCapacityDailyPeak>();
            while (await reader.ReadAsync())
                peaks.Add(new HighCapacityDailyPeak(
                    DateOnly.FromDateTime(reader.GetDateTime("shift_date")),
                    Int64OrZero(reader, "parcels_per_hour"), reader.GetDateTime("window_start"),
                    Int64OrZero(reader, "total_parcels")));
            var sortedPeaks = peaks.Select(x => x.PeakPerHour).Order().ToArray();
            var practicalCapacity = sortedPeaks.Length == 0 ? 0 : sortedPeaks[Math.Max(0, (int)Math.Ceiling(sortedPeaks.Length * 0.75) - 1)];
            capacityBenchmarkCache = new CapacityBenchmarkSnapshot(DateTimeOffset.Now, practicalCapacity, sortedPeaks.DefaultIfEmpty(0).Max(), peaks);
            return capacityBenchmarkCache;
        }
        finally
        {
            capacityBenchmarkLock.Release();
        }
    }

    private static IReadOnlyList<HighCapacityGap> BuildHighCapacityGaps(
        DateTime? firstScan, DateTime analysisEnd, IReadOnlyDictionary<DateTime, long> minuteCounts, long practicalCapacityPerHour)
    {
        if (firstScan is null || practicalCapacityPerHour <= 0) return [];
        var firstWindow = firstScan.Value;
        var lowWindows = new List<(DateTime Start, DateTime End, long Parcels)>();
        for (var start = firstWindow; start.AddMinutes(5) <= analysisEnd; start = start.AddMinutes(5))
        {
            var end = start.AddMinutes(5);
            var parcels = minuteCounts.Where(x => x.Key >= start && x.Key < end).Sum(x => x.Value);
            if (60m * parcels / 5 < practicalCapacityPerHour * 0.4m) lowWindows.Add((start, end, parcels));
        }
        if (lowWindows.Count == 0) return [];
        var gaps = new List<HighCapacityGap>();
        var gapStart = lowWindows[0].Start;
        var gapEnd = lowWindows[0].End;
        long gapParcels = lowWindows[0].Parcels;
        foreach (var window in lowWindows.Skip(1))
        {
            if (window.Start == gapEnd)
            {
                gapEnd = window.End;
                gapParcels += window.Parcels;
                continue;
            }
            gaps.Add(CreateGap(gapStart, gapEnd, gapParcels, practicalCapacityPerHour));
            gapStart = window.Start;
            gapEnd = window.End;
            gapParcels = window.Parcels;
        }
        gaps.Add(CreateGap(gapStart, gapEnd, gapParcels, practicalCapacityPerHour));

        // The chronological edges normally represent startup and shutdown,
        // rather than interruptions during active production.
        var productionGaps = gaps.Count <= 2
            ? Enumerable.Empty<HighCapacityGap>()
            : gaps.Skip(1).Take(gaps.Count - 2);
        return productionGaps.OrderByDescending(x => x.DurationMinutes).ThenBy(x => x.Start).ToArray();
    }

    private static HighCapacityGap CreateGap(DateTime start, DateTime end, long parcels, long practicalCapacityPerHour)
    {
        var duration = (int)(end - start).TotalMinutes;
        var average = Math.Round(60m * parcels / duration, 0);
        return new HighCapacityGap(start, end, duration, parcels, average, Math.Round(100m * average / practicalCapacityPerHour, 1));
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
