using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

sealed record EdiForecastSnapshot(string Id, DateTimeOffset SavedAt, DateOnly ScheduledDate,
    string ModelVersion, string Origin, EdiForecastResponse Forecast, EdiMlForecastResponse? MlForecast = null);
sealed record EdiForecastComparison(string SnapshotId, DateTimeOffset SavedAt, string ModelVersion,
    DateOnly Date, int Horizon, long? Predicted, long? Actual, long? Difference, double? ErrorPercent,
    long? MlPredicted = null, long? MlDifference = null, double? MlErrorPercent = null);
sealed record EdiForecastPriorDay(EdiForecastDay Day, EdiMlForecastDay? MlDay, EdiForecastComparison Comparison);
sealed record EdiForecastArchiveView(EdiForecastSnapshot? Snapshot, DateTimeOffset NextRefresh,
    string Mode, IReadOnlyList<EdiForecastComparison> Comparisons, bool RefreshPending, EdiForecastPriorDay? PreviousDay,
    DateTimeOffset ForecastNextRefresh);
sealed record EdiForecastActuals(DateOnly AsOfDate, DateTimeOffset UpdatedAt, IReadOnlyList<EdiHistoryDay> Days);

sealed class EdiForecastArchive(IWebHostEnvironment environment, EdiMlForecastService? mlForecast = null)
{
    public const string ModelVersion = "weekly-saturday-v6-lightgbm-challenger";
    private readonly string directory = Environment.GetEnvironmentVariable("EDI_FORECAST_PATH")
        ?? Path.Combine(environment.ContentRootPath, "App_Data", "edi-forecasts");
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim actualsGate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Modified, EdiForecastSnapshot Snapshot)> snapshotCache = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
    public static DateTime LocalNow => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).DateTime;
    public static DateOnly DueDate(DateTime localNow) => DateOnly.FromDateTime(localNow.Hour < 6 ? localNow.AddDays(-1) : localNow);
    public static DateOnly ForecastWeekStart(DateOnly date)
        => date.AddDays(-((7 + (int)date.DayOfWeek - (int)DayOfWeek.Saturday) % 7));
    public static DateOnly ForecastDueDate(DateTime localNow)
    {
        var date = DateOnly.FromDateTime(localNow);
        var saturday = ForecastWeekStart(date);
        return date.DayOfWeek == DayOfWeek.Saturday && localNow.Hour < 6 ? saturday.AddDays(-7) : saturday;
    }
    public static DateTimeOffset NextRefresh(DateTime localNow)
    {
        var next = localNow.Date.AddHours(6);
        if (next <= localNow) next = next.AddDays(1);
        return new(next, Zone.GetUtcOffset(next));
    }
    public static DateTimeOffset NextForecastRefresh(DateTime localNow)
    {
        var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)localNow.DayOfWeek + 7) % 7;
        var next = localNow.Date.AddDays(daysUntilSaturday).AddHours(6);
        if (next <= localNow) next = next.AddDays(7);
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
            var due = ForecastDueDate(now);
            var id = $"{due:yyyy-MM-dd}-v6";
            var existing = Read().FirstOrDefault(s => s.Id == id);
            if (existing != null) return existing;
            // A late start may rebuild the current week, but the historical cutoff remains Saturday.
            var historyDays = mlForecast?.RequiredHistoryDays(due) ?? EdiForecast.HistoryDays;
            var history = await data.GetEdiHistoryAsync(due.AddDays(-historyDays), due);
            if (history.Count == 0) throw new InvalidOperationException("Historique EDI vide; prévision précédente conservée.");
            var forecast = EdiForecast.Build(due, history);
            var ml = mlForecast == null ? null : await mlForecast.BuildAsync(due, history);
            var snapshot = new EdiForecastSnapshot(id, DateTimeOffset.UtcNow, due, ModelVersion,
                "Prévision hebdomadaire du samedi à 6 h, figée jusqu'au vendredi (ou rattrapage au démarrage)", forecast, ml);
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
        var weekStart = analysisDate == operationalDate ? ForecastDueDate(now) : ForecastWeekStart(analysisDate);
        var selected = snapshots.FirstOrDefault(s => s.ModelVersion == ModelVersion && s.ScheduledDate == weekStart)
            ?? (analysisDate == operationalDate ? null : snapshots.FirstOrDefault(s => s.ScheduledDate == analysisDate));
        // Daily files retain observations beyond the latest 84-day window.
        var byDate = new Dictionary<DateOnly, EdiHistoryDay>();
        var actualsDirectory = Path.Combine(directory, "actuals");
        if (Directory.Exists(actualsDirectory))
            foreach (var path in Directory.EnumerateFiles(actualsDirectory, "*.json").Order())
            {
                var saved = JsonSerializer.Deserialize<EdiForecastActuals>(File.ReadAllText(path), Json);
                if (saved != null)
                    foreach (var day in saved.Days) byDate[day.Date] = day;
            }
        foreach (var day in actuals) byDate[day.Date] = day;
        var rows = snapshots.SelectMany(snapshot => snapshot.Forecast.Days.Select(day =>
        {
            // Weekly V6 forecasts Saturday-Friday using only data prior to Saturday.
            // V4/V5 deliberately forecast day zero at 6am using only prior completed days.
            // Legacy versions retain their original pre-day eligibility rule.
            var savedLocal = TimeZoneInfo.ConvertTime(snapshot.SavedAt, Zone).DateTime;
            var weeklyForecast = snapshot.ModelVersion == ModelVersion && snapshot.ScheduledDate == ForecastWeekStart(day.Date)
                && snapshot.Forecast.AsOfDate == snapshot.ScheduledDate;
            var dailyDayZeroModel = snapshot.ModelVersion is "weekday-annual-v5-lightgbm-challenger" or "weekday-annual-v4-today-cyber-monday";
            var sameDayForecast = dailyDayZeroModel && snapshot.ScheduledDate == day.Date
                && snapshot.Forecast.AsOfDate == day.Date
                && savedLocal >= day.Date.ToDateTime(new TimeOnly(6, 0))
                && savedLocal < day.Date.AddDays(1).ToDateTime(new TimeOnly(4, 0));
            long? actual = day.Date < operationalDate && (weeklyForecast || savedLocal < day.Date.ToDateTime(new TimeOnly(4, 0)) || sameDayForecast)
                && byDate.TryGetValue(day.Date, out var observed) ? observed.Parcels : null;
            long? difference = actual.HasValue && day.Parcels.HasValue ? actual.Value - day.Parcels.Value : null;
            var mlPredicted = snapshot.MlForecast?.Days.FirstOrDefault(candidate => candidate.Date == day.Date)?.Parcels;
            long? mlDifference = actual.HasValue && mlPredicted.HasValue ? actual.Value - mlPredicted.Value : null;
            return new EdiForecastComparison(snapshot.Id, snapshot.SavedAt, snapshot.ModelVersion, day.Date,
                day.Date.DayNumber - snapshot.Forecast.AsOfDate.DayNumber, day.Parcels, actual, difference,
                actual > 0 && difference.HasValue ? Math.Round(Math.Abs((double)difference.Value) / actual.Value * 100, 1) : null,
                mlPredicted, mlDifference,
                actual > 0 && mlDifference.HasValue ? Math.Round(Math.Abs((double)mlDifference.Value) / actual.Value * 100, 1) : null);
        })).OrderByDescending(r => r.Date).ThenByDescending(r => r.SavedAt).ToArray();
        var previousDate = analysisDate.AddDays(-1);
        var previousComparison = rows.Where(row => row.Date == previousDate && row.Actual.HasValue)
            .OrderByDescending(row => row.SavedAt).FirstOrDefault();
        var previousSnapshot = previousComparison == null ? null : snapshots.FirstOrDefault(snapshot => snapshot.Id == previousComparison.SnapshotId);
        var previousDay = previousSnapshot?.Forecast.Days.FirstOrDefault(day => day.Date == previousDate);
        var previous = previousComparison == null || previousDay == null ? null : new EdiForecastPriorDay(previousDay,
            previousSnapshot?.MlForecast?.Days.FirstOrDefault(day => day.Date == previousDate), previousComparison);
        return new(selected, NextRefresh(now), selected == null ? "Reconstitution non archivée" : "Prévision sauvegardée", rows,
            analysisDate == operationalDate && (selected == null || selected.ScheduledDate != ForecastDueDate(now) || selected.ModelVersion != ModelVersion),
            previous, NextForecastRefresh(now));
    }
}

sealed class EdiForecastRefreshService(ConveyorDataService data, EdiForecastArchive archive,
    ILogger<EdiForecastRefreshService> logger) : BackgroundService
{
    internal static TimeSpan RefreshDelay(DateTime iterationStarted, DateTimeOffset completedAt, bool retry)
    {
        if (retry) return TimeSpan.FromMinutes(1);
        // Keep the deadline from before the reads: crossing 6 am must trigger a catch-up,
        // not silently move the next check to tomorrow.
        var delay = EdiForecastArchive.NextRefresh(iterationStarted) - completedAt;
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var iterationStarted = EdiForecastArchive.LocalNow;
            var retry = false;
            try
            {
                var snapshot = await archive.EnsureCurrentAsync(data);
                await archive.EnsureActualsAsync(data);
                logger.LogInformation("Relevé EDI terminé : prévision {SnapshotId}, début {StartedAt}, fin {CompletedAt} (Montréal).",
                    snapshot.Id, iterationStarted, EdiForecastArchive.LocalNow);
            }
            catch (Exception ex) { retry = true; logger.LogError(ex, "Renouvellement EDI impossible; archives conservées, nouvelle tentative dans une minute."); }
            var delay = RefreshDelay(iterationStarted, DateTimeOffset.UtcNow, retry);
            try { await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
