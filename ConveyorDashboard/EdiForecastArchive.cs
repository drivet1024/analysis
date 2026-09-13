using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

sealed record EdiForecastSnapshot(string Id, DateTimeOffset SavedAt, DateOnly ScheduledDate,
    string ModelVersion, string Origin, EdiForecastResponse Forecast);
sealed record EdiForecastComparison(string SnapshotId, DateTimeOffset SavedAt, string ModelVersion,
    DateOnly Date, int Horizon, long? Predicted, long? Actual, long? Difference, double? ErrorPercent);
sealed record EdiForecastArchiveView(EdiForecastSnapshot? Snapshot, DateTimeOffset NextRefresh,
    string Mode, IReadOnlyList<EdiForecastComparison> Comparisons, bool RefreshPending);
sealed record EdiForecastActuals(DateOnly AsOfDate, DateTimeOffset UpdatedAt, IReadOnlyList<EdiHistoryDay> Days);

sealed class EdiForecastArchive(IWebHostEnvironment environment)
{
    public const string ModelVersion = "weekday-annual-v4-today-cyber-monday";
    private readonly string directory = Environment.GetEnvironmentVariable("EDI_FORECAST_PATH")
        ?? Path.Combine(environment.ContentRootPath, "App_Data", "edi-forecasts");
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim actualsGate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Modified, EdiForecastSnapshot Snapshot)> snapshotCache = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
    public static DateTime LocalNow => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).DateTime;
    public static DateOnly DueDate(DateTime localNow) => DateOnly.FromDateTime(localNow.Hour < 6 ? localNow.AddDays(-1) : localNow);
    public static DateTimeOffset NextRefresh(DateTime localNow)
    {
        var next = localNow.Date.AddHours(6);
        if (next <= localNow) next = next.AddDays(1);
        return new(next, Zone.GetUtcOffset(next));
    }

    public IReadOnlyList<EdiForecastSnapshot> Read()
    {
        Directory.CreateDirectory(directory);
        return Directory.EnumerateFiles(directory, "*.json").OrderDescending()
            .Select(path =>
            {
                var modified = File.GetLastWriteTimeUtc(path);
                if (snapshotCache.TryGetValue(path, out var cached) && cached.Modified == modified) return cached.Snapshot;
                var snapshot = JsonSerializer.Deserialize<EdiForecastSnapshot>(File.ReadAllText(path), Json)
                    ?? throw new InvalidDataException($"Archive EDI illisible : {Path.GetFileName(path)}");
                snapshotCache[path] = (modified, snapshot);
                return snapshot;
            })
            .OrderByDescending(s => s.SavedAt).ToArray();
    }

    public EdiForecastActuals? ReadActuals()
    {
        var actualsDirectory = Path.Combine(directory, "actuals");
        if (!Directory.Exists(actualsDirectory)) return null;
        var path = Directory.EnumerateFiles(actualsDirectory, "*.json").OrderDescending().FirstOrDefault();
        return path == null ? null : JsonSerializer.Deserialize<EdiForecastActuals>(File.ReadAllText(path), Json);
    }

    public async Task EnsureActualsAsync(ConveyorDataService data)
    {
        await actualsGate.WaitAsync();
        try
        {
            var due = DueDate(LocalNow);
            var path = Path.Combine(directory, "actuals", $"{due:yyyy-MM-dd}.json");
            if (File.Exists(path)) return;
            var days = await data.GetEdiHistoryAsync(due.AddDays(-84), due);
            if (days.Count == 0) throw new InvalidOperationException("Réel EDI vide; dernier relevé conservé.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new EdiForecastActuals(due, DateTimeOffset.UtcNow, days), Json));
                File.Move(temporary, path, overwrite: false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { actualsGate.Release(); }
    }

    // Append-only files: current and older versions are already durable before replacement.
    public async Task<EdiForecastSnapshot> EnsureCurrentAsync(ConveyorDataService data)
    {
        await gate.WaitAsync();
        try
        {
            var now = LocalNow;
            var due = DueDate(now);
            var id = $"{due:yyyy-MM-dd}-v4";
            var existing = Read().FirstOrDefault(s => s.Id == id);
            if (existing != null) return existing;
            // Do not invent a 6 am snapshot if the service starts later: save the actual timestamp.
            var history = await data.GetEdiHistoryAsync(due.AddDays(-EdiForecast.HistoryDays), due);
            if (history.Count == 0) throw new InvalidOperationException("Historique EDI vide; prévision précédente conservée.");
            var forecast = EdiForecast.Build(due, history);
            var snapshot = new EdiForecastSnapshot(id, DateTimeOffset.UtcNow, due, ModelVersion,
                "Renouvellement quotidien à 6 h (ou rattrapage au démarrage)", forecast);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, id + ".json");
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot, Json));
                File.Move(temporary, path, overwrite: false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return snapshot;
        }
        finally { gate.Release(); }
    }

    public EdiForecastArchiveView View(DateOnly analysisDate, IReadOnlyList<EdiHistoryDay> actuals)
    {
        var now = LocalNow;
        var snapshots = Read();
        var operationalDate = DateOnly.FromDateTime(now.Hour < 4 ? now.AddDays(-1) : now);
        var selected = analysisDate == operationalDate ? snapshots.FirstOrDefault(s => s.ModelVersion == ModelVersion) ?? snapshots.FirstOrDefault()
            : snapshots.FirstOrDefault(s => s.ScheduledDate == analysisDate);
        var byDate = actuals.ToDictionary(d => d.Date);
        var rows = snapshots.Take(30).SelectMany(snapshot => snapshot.Forecast.Days.Select(day =>
        {
            // V4 deliberately forecasts day zero at 6am using only prior completed days.
            // Legacy versions retain their original pre-day eligibility rule.
            var savedLocal = TimeZoneInfo.ConvertTime(snapshot.SavedAt, Zone).DateTime;
            var sameDayForecast = snapshot.ModelVersion == ModelVersion && snapshot.ScheduledDate == day.Date
                && snapshot.Forecast.AsOfDate == day.Date
                && savedLocal >= day.Date.ToDateTime(new TimeOnly(6, 0))
                && savedLocal < day.Date.AddDays(1).ToDateTime(new TimeOnly(4, 0));
            long? actual = day.Date < operationalDate && (savedLocal < day.Date.ToDateTime(new TimeOnly(4, 0)) || sameDayForecast)
                && byDate.TryGetValue(day.Date, out var observed) ? observed.Parcels : null;
            long? difference = actual.HasValue && day.Parcels.HasValue ? actual.Value - day.Parcels.Value : null;
            return new EdiForecastComparison(snapshot.Id, snapshot.SavedAt, snapshot.ModelVersion, day.Date,
                day.Date.DayNumber - snapshot.Forecast.AsOfDate.DayNumber, day.Parcels, actual, difference,
                actual > 0 && difference.HasValue ? Math.Round(Math.Abs((double)difference.Value) / actual.Value * 100, 1) : null);
        })).OrderByDescending(r => r.Date).ThenByDescending(r => r.SavedAt).ToArray();
        return new(selected, NextRefresh(now), selected == null ? "Reconstitution non archivée" : "Prévision sauvegardée", rows,
            analysisDate == operationalDate && (selected == null || selected.ScheduledDate < DueDate(now) || selected.ModelVersion != ModelVersion));
    }
}

sealed class EdiForecastRefreshService(ConveyorDataService data, EdiForecastArchive archive,
    ILogger<EdiForecastRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var retry = false;
            try { await archive.EnsureCurrentAsync(data); await archive.EnsureActualsAsync(data); }
            catch (Exception ex) { retry = true; logger.LogError(ex, "Renouvellement EDI impossible; archives conservées, nouvelle tentative dans une minute."); }
            var delay = retry ? TimeSpan.FromMinutes(1) : EdiForecastArchive.NextRefresh(EdiForecastArchive.LocalNow) - DateTimeOffset.UtcNow;
            try { await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
