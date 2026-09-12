using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

sealed record EdiSectorActualSnapshot(DateOnly AsOf, DateTimeOffset UpdatedAt, EdiSectorHistorySet Source);

sealed class EdiSectorWeeklyService(EdiSectorForecastService forecasts, IWebHostEnvironment environment, TimeProvider? clock = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string directory = Path.Combine(Environment.GetEnvironmentVariable("EDI_FORECAST_PATH")
        ?? Path.Combine(environment.ContentRootPath, "App_Data", "edi-forecasts"), "sectors-st-hubert", "weekly-saturday");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private EdiSectorForecastResponse? cached;

    private static async Task SaveOnceAsync<T>(string path, T value, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, Json), token);
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<EdiSectorForecastResponse> GetAsync(DateOnly selected, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var utcNow = (clock ?? TimeProvider.System).GetUtcNow();
            var now = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utcNow, "America/Toronto").DateTime;
            var due = EdiForecastArchive.DueDate(now);
            var saturday = EdiSectorWeek.Saturday(selected);
            // Saturday already displays the coming Monday-Friday, even before the 6 am calculation.
            var monday = saturday.AddDays(2);
            var friday = saturday.AddDays(6);
            if (cached?.AsOfDate == saturday && cached.Weekly?.ActualsAsOf == due) return cached;
            var forecastPath = Path.Combine(directory, $"{saturday:yyyy-MM-dd}-forecast.json");
            EdiSectorForecastResponse? forecast = null;
            if (File.Exists(forecastPath))
                forecast = JsonSerializer.Deserialize<EdiSectorForecastResponse>(await File.ReadAllTextAsync(forecastPath, cancellationToken), Json)!;
            else if (EdiSectorWeek.CanCreate(saturday, now))
            {
                forecast = await forecasts.GetAsync(saturday, persist: true, cancellationToken: cancellationToken);
                // Never relabel a reconstruction or a snapshot made after the forecast week started.
                var savedLocal = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(forecast.SavedAt, "America/Toronto");
                if (!forecast.Reconstructed && DateOnly.FromDateTime(savedLocal.DateTime) == saturday)
                    await SaveOnceAsync(forecastPath, forecast, cancellationToken);
                else forecast = null;
            }
            var available = forecast != null;
            forecast ??= await forecasts.EmptyWeekAsync(saturday, cancellationToken);
            var actualPath = Path.Combine(directory, "actuals", $"{saturday:yyyy-MM-dd}-{due:yyyy-MM-dd}.json");
            EdiSectorActualSnapshot actual;
            if (File.Exists(actualPath))
                actual = JsonSerializer.Deserialize<EdiSectorActualSnapshot>(await File.ReadAllTextAsync(actualPath, cancellationToken), Json)!;
            else
            {
                var end = due.AddDays(1) < saturday.AddDays(7) ? due.AddDays(1) : saturday.AddDays(7);
                var source = end > monday
                    ? await forecasts.ReadHistoryAsync(monday, end, due.ToDateTime(new TimeOnly(6, 0)), cancellationToken)
                    : new EdiSectorHistorySet([], [], 0, 0, 0, 0);
                actual = new(due, utcNow, source);
                await SaveOnceAsync(actualPath, actual, cancellationToken);
            }
            var sectors = forecast.Sectors.Select(s => s with
            {
                Actuals = Enumerable.Range(1, 5).Select(i => EdiSectorWeek.Actual(saturday.AddDays(i + 1), s.SectorId, due, actual.Source)).ToArray()
            }).ToArray();
            // Missing forecast weeks still show actuals and current sector descriptions, never invented predictions.
            if (!available) forecast = forecast with { HistoryStart = monday, ObservedNetworkDays = actual.Source.ObservedDates.Count,
                NetworkParcels = actual.Source.Total, OutsideDepotParcels = actual.Source.Outside + actual.Source.Rows.Where(r => !sectors.Any(s => s.SectorId == r.SectorId)).Sum(r => r.Parcels),
                UnmappedParcels = actual.Source.Unmapped, AmbiguousPostalParcels = actual.Source.Ambiguous,
                Sectors = sectors.Select(s => s with {
                    HistoricalParcels = actual.Source.Rows.Where(r => r.SectorId == s.SectorId).Sum(r => r.Parcels),
                    PostalParcels = actual.Source.Rows.Where(r => r.SectorId == s.SectorId).Sum(r => r.PostalParcels),
                    RouteFallbackParcels = actual.Source.Rows.Where(r => r.SectorId == s.SectorId).Sum(r => r.RouteFallbackParcels)
                }).ToArray() };
            else forecast = forecast with { Sectors = sectors };
            forecast = await forecasts.WithCurrentSectorDetailsAsync(forecast, cancellationToken);
            cached = forecast with { Weekly = new(monday, friday, available, due, actual.UpdatedAt, EdiForecastArchive.NextRefresh(now)) };
            return cached;
        }
        finally { gate.Release(); }
    }
}

sealed class EdiSectorForecastWorker(EdiSectorWeeklyService forecasts, ILogger<EdiSectorForecastWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var retry = false;
            try { await forecasts.GetAsync(EdiForecastArchive.DueDate(EdiForecastArchive.LocalNow), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { retry = true; logger.LogError(ex, "Prévisions hebdomadaires ou réel indisponibles; archives conservées."); }
            var delay = retry ? TimeSpan.FromMinutes(15) : EdiForecastArchive.NextRefresh(EdiForecastArchive.LocalNow) - DateTimeOffset.UtcNow;
            try { await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
