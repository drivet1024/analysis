if (args.Length == 3)
{
    using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(args[0]));
    var sourceRows = document.RootElement.EnumerateArray().Select(row => new EdiHistoryDay(
        DateOnly.FromDateTime(row.GetProperty("date").GetDateTime()), row.GetProperty("parcels").GetInt64())).ToArray();
    var result = EdiForecast.Build(DateOnly.Parse(args[1]), sourceRows);
    File.WriteAllText(args[2], System.Text.Json.JsonSerializer.Serialize(result,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true }));
    Console.WriteLine($"Forecast {args[1]}: total={result.Total}, WAPE={result.BacktestWape}%, baseline={result.Seasonality?.BaselineWape}%.");
    return;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
var asOf = new DateOnly(2026, 9, 12);
var sectorDates = Enumerable.Range(1, 455).Select(i => asOf.AddDays(-i)).ToArray();
var sectorHistory = sectorDates.Select(date => new EdiSectorHistory(date, 530, 100, 90, 10, 2)).ToArray();
var sectorForecast = EdiSectorModel.Build(asOf, 530, sectorHistory, sectorDates);
Check(sectorForecast.Days.All(day => day.Parcels == (EdiSectorCalendar.IsWeekend(day.Date) ? 0 : 100)), "Stable sector volumes forecast independently");
Check(EdiSectorModel.Build(asOf, 999, sectorHistory, sectorDates).Days.Where(day => !EdiSectorCalendar.IsWeekend(day.Date)).All(day => day.Parcels == null), "Absent sector history is unknown, not zero");
var sparseSectors = EdiSectorModel.Build(asOf, 530, sectorHistory.Where(row => row.Date.DayOfWeek != DayOfWeek.Wednesday).ToArray(), sectorDates);
Check(sparseSectors.Days.Single(day => day.Date.DayOfWeek == DayOfWeek.Wednesday).Parcels == 0, "Observed network days without sector parcels are zero after first sector activity");
var newSector = EdiSectorModel.Build(asOf, 530, sectorHistory.Where(row => row.Date >= asOf.AddDays(-7)).ToArray(), sectorDates);
Check(newSector.Days.Where(day => !EdiSectorCalendar.IsWeekend(day.Date)).All(day => day.Parcels == null), "Pre-launch dates do not manufacture historical zero observations");
var missingNetwork = EdiSectorModel.Build(asOf, 530, sectorHistory.Where(row => row.Date.DayOfWeek != DayOfWeek.Wednesday).ToArray(), sectorDates.Where(date => date.DayOfWeek != DayOfWeek.Wednesday).ToArray());
Check(missingNetwork.Days.Single(day => day.Date.DayOfWeek == DayOfWeek.Wednesday).Parcels == null, "Days absent from entire source are not zero-filled");

Check(EdiSectorCalendar.DeliveryDate(new DateTime(2026,9,14,15,0,0)) == new DateOnly(2026,9,15), "Monday evening delivers Tuesday");
Check(EdiSectorCalendar.DeliveryDate(new DateTime(2026,9,15,2,59,59)) == new DateOnly(2026,9,15), "Overnight scan retains prior shift");
Check(EdiSectorCalendar.DeliveryDate(new DateTime(2026,9,15,3,0,0)) == null, "3am closes the sorting window");
Check(EdiSectorCalendar.DeliveryDate(new DateTime(2026,9,14,14,59,59)) == null, "Before 3pm is outside evening shift");
Check(EdiSectorCalendar.DeliveryDate(new DateTime(2026,9,11,20,0,0)) == new DateOnly(2026,9,14)
    && EdiSectorCalendar.DeliveryDate(new DateTime(2026,9,12,2,0,0)) == new DateOnly(2026,9,14)
    && EdiSectorCalendar.DeliveryDate(new DateTime(2026,9,13,20,0,0)) == new DateOnly(2026,9,14), "Friday and Sunday share Monday including Friday after midnight");
Check(EdiSectorCalendar.DeliveryDate(new DateTime(2026,9,12,20,0,0)) == null
    && EdiSectorCalendar.DeliveryDate(new DateTime(2026,9,13,2,0,0)) == null, "Saturday evening shift excluded");
Check(EdiSectorCalendar.Holiday(new DateOnly(2026,9,8)) != null, "Holiday sorting evenings do not contaminate following delivery day");
Check(EdiSeasonality.PreviousReference(new DateOnly(2026,12,1), true) == new DateOnly(2025,12,2), "Cyber Monday sorting aligns following Tuesday year over year");
Check(EdiSeasonality.PreviousReference(new DateOnly(2026,11,30), true) == new DateOnly(2025,12,1), "Black Friday plus Sunday aligns to Monday delivery");
// Incomplete Mondays may contain Friday parcels, but cannot become training observations.
var incompleteMondayDates = sectorDates.Where(d => d.DayOfWeek != DayOfWeek.Monday).ToArray();
var incompleteMondays = EdiSectorModel.Build(asOf, 530, sectorHistory, incompleteMondayDates);
Check(incompleteMondays.Days.Single(d => d.Date.DayOfWeek == DayOfWeek.Monday).Parcels == null,
    "Friday-only Mondays cannot train even when sector rows exist");
var futureSector = EdiSectorModel.Build(asOf, 530, sectorHistory.Concat(new[] {
    new EdiSectorHistory(asOf.AddDays(2), 530, 999999, 999999, 0, 0) }).ToArray(), sectorDates.Append(asOf.AddDays(2)).ToArray());
Check(futureSector.Total == sectorForecast.Total, "Friday parcels assigned to future Monday cannot leak into training");
var sectorCyberAsOf = new DateOnly(2026,11,28);
var sectorCyberDays = Enumerable.Range(1,455).Select(i => sectorCyberAsOf.AddDays(-i)).Where(d => !EdiSectorCalendar.IsWeekend(d)).ToArray();
var sectorCyberHistory = sectorCyberDays.Select(d => new EdiSectorHistory(d,530,
    d == new DateOnly(2025,12,2) ? 2000 : 100,0,0,0)).ToArray();
var cyberDelivery = EdiSectorModel.Build(sectorCyberAsOf,530,sectorCyberHistory,sectorCyberDays);
Check(cyberDelivery.Days.Single(d => d.Date == new DateOnly(2026,12,1)).Parcels == 2000,
    "Cyber Monday sorting peak is forecast on Tuesday delivery using last year's Tuesday");
Check(cyberDelivery.Days.Single(d => d.Date == new DateOnly(2026,11,30)).Parcels == 100,
    "Tuesday Cyber peak must not be applied to Monday's Friday/Sunday cohort");
var intraday = Enumerable.Range(1, 8).Select(i => new EdiIntradaySample(asOf.AddDays(-7 * i), 600, 1000, 0)).ToArray();
var nowcast = EdiNowcast.Build(asOf, asOf.ToDateTime(new TimeOnly(15, 0)), 4000, intraday);
Check(nowcast.EstimatedFinal == 6667 && nowcast.Remaining == 2667 && nowcast.HistoricalProgressPercent == 60,
    "Intraday projection uses historical completion proportion");
Check(EdiNowcast.Build(asOf, asOf.ToDateTime(new TimeOnly(4, 30)), 10, intraday).EstimatedFinal == null,
    "No unstable projection at opening");
Check(EdiNowcast.Build(asOf, asOf.ToDateTime(new TimeOnly(15, 0)), 10, intraday.Take(3).ToArray()).EstimatedFinal == null,
    "Intraday requires four similar days");
Check(EdiNowcast.Build(asOf, asOf.ToDateTime(new TimeOnly(15, 0)), 10,
    intraday.Select(d => d with { ParcelsAtSameTime = 0 }).ToArray()).EstimatedFinal == null, "No division by zero");
Check(EdiNowcast.Build(asOf, asOf.AddDays(1).ToDateTime(new TimeOnly(4, 0)), 8000, [], completed: true).EstimatedFinal == 8000,
    "Completed days show actual final count");
var finalProjection = EdiNowcast.Build(asOf, asOf.ToDateTime(new TimeOnly(23, 59)), 9000,
    intraday.Select(d => d with { ParcelsAtSameTime = 1000 }).ToArray());
Check(finalProjection.EstimatedFinal == 9000 && finalProjection.Remaining == 0, "Projection cannot fall below parcels already created");
var monday = new DateOnly(2026, 9, 14);
var holidaySamples = Enumerable.Range(1, 8).Select(i => new EdiIntradaySample(monday.AddDays(-7 * i), 600, 1000, 0)).ToArray();
Check(EdiNowcast.Build(monday, monday.ToDateTime(new TimeOnly(15, 0)), 4000, holidaySamples).Samples.All(d => d.Date != new DateOnly(2026, 9, 7)), "Holiday profiles excluded");
var history = Enumerable.Range(1, 84).Select(i => new EdiHistoryDay(asOf.AddDays(-i),
    100 + (int)asOf.AddDays(-i).DayOfWeek * 10)).ToArray();
var forecast = EdiForecast.Build(asOf, history);
Check(forecast.Days.Count == 7 && forecast.Days[0].Date == asOf
    && forecast.Days[6].Date == asOf.AddDays(6), "Today plus six days");
Check(forecast.Days.All(d => d.Parcels == 100 + (int)d.Date.DayOfWeek * 10), "Weekly seasonality");
Check(forecast.BacktestDays == 27 && forecast.BacktestMae == 0 && forecast.BacktestWape == 0, "Historical evaluation excludes Labour Day");
Check(forecast.Total == forecast.Days.Sum(d => d.Parcels), "Total reconciles");
var contaminated = EdiForecast.Build(asOf, history.Concat(new[] {
    new EdiHistoryDay(asOf, 999999), new EdiHistoryDay(asOf.AddDays(1), 999999) }).ToArray());
Check(contaminated.Total == forecast.Total && contaminated.BacktestMae == forecast.BacktestMae, "Exclude current and future days");
var empty = EdiForecast.Build(asOf, Array.Empty<EdiHistoryDay>());
Check(empty.Total == null && empty.Days.All(d => d.Parcels == null) && empty.BacktestDays == 0, "Missing data is not zero");
var zero = EdiForecast.Build(asOf, history.Select(d => d with { Parcels = 0 }).ToArray());
Check(zero.Total == 0 && zero.BacktestMae == 0 && zero.BacktestWape == null, "Observed zeros remain zero");
var sparse = EdiForecast.Build(asOf, history.Take(21).ToArray());
Check(sparse.Days.All(d => d.Parcels == null), "Minimum four comparable observations");
var ramp = EdiForecast.Build(asOf, history.Select(d => d with { Parcels = d.Date >= asOf.AddDays(-7) ? 800 : 80 }).ToArray());
Check(ramp.Days.Where(d => d.Date.DayOfWeek != DayOfWeek.Monday).All(d => d.Parcels == 240), "Most recent observation receives weight eight of 36");
Check(ramp.Days.Single(d => d.Date.DayOfWeek == DayOfWeek.Monday).Parcels == 80, "Holiday spike cannot contaminate Monday forecast");
Check(forecast.Days.SelectMany(d => d.Samples).All(d => EdiHolidayCalendar.Name(d.Date) == null), "All training samples exclude holidays");
Check(EdiHolidayCalendar.Name(new DateOnly(2026, 4, 3)) == "Vendredi saint"
    && EdiHolidayCalendar.Name(new DateOnly(2026, 4, 6)) == "Lundi de Pâques"
    && EdiHolidayCalendar.Name(new DateOnly(2026, 5, 18)) != null, "Movable holidays");
var holidayFuture = EdiForecast.Build(new DateOnly(2026, 9, 6), history);
Check(holidayFuture.Days[1].Holiday != null && holidayFuture.Days[1].Parcels == null && holidayFuture.Total == null, "Future holiday must not receive an ordinary estimate");
Check(EdiForecastArchive.DueDate(new DateTime(2026, 9, 12, 5, 59, 59)) == asOf.AddDays(-1)
    && EdiForecastArchive.DueDate(new DateTime(2026, 9, 12, 6, 0, 0)) == asOf, "6 am boundary");
Check(EdiForecastArchive.NextRefresh(new DateTime(2026, 3, 7, 12, 0, 0)).Offset == TimeSpan.FromHours(-4)
    && EdiForecastArchive.NextRefresh(new DateTime(2026, 10, 31, 12, 0, 0)).Offset == TimeSpan.FromHours(-5), "DST-aware next refresh");

Check(EdiSeasonality.CyberMonday(2025) == new DateOnly(2025, 12, 1)
    && EdiSeasonality.CyberMonday(2026) == new DateOnly(2026, 11, 30)
    && EdiSeasonality.CyberMonday(2024) == new DateOnly(2024, 12, 2), "Cyber Monday follows the fourth Thursday of November");
Check(EdiSeasonality.PreviousReference(new DateOnly(2026, 11, 30)) == new DateOnly(2025, 12, 1)
    && EdiSeasonality.PreviousReference(new DateOnly(2026, 11, 27)) == new DateOnly(2025, 11, 28), "Commercial-event alignment");
var annualHistory = Enumerable.Range(1, 455).Select(i => new EdiHistoryDay(asOf.AddDays(-i), i < 180 ? 200 : 100)).ToArray();
var annualForecast = EdiForecast.Build(asOf, annualHistory);
Check(annualForecast.Days.All(d => d.Annual?.GrowthFactor == 2 && d.Parcels == 200), "Annual growth is normalized before blending");
var seasonalReference = asOf.AddDays(7 - 364);
var seasonalShape = EdiForecast.Build(asOf.AddDays(1), annualHistory.Select(d =>
    new[] { seasonalReference.AddDays(-7), seasonalReference, seasonalReference.AddDays(7) }.Contains(d.Date)
        ? d with { Parcels = 300 } : d).ToArray());
Check(seasonalShape.Days[6].Parcels == 400 && seasonalShape.Days[6].Annual!.GrowthFactor == 2,
    "Annual seasonal shape contributes half the ordinary-day forecast");
var cyberAsOf = new DateOnly(2026, 11, 29);
var cyberHistory = Enumerable.Range(1, 455).Select(i => cyberAsOf.AddDays(-i)).Select(date =>
    new EdiHistoryDay(date, date == new DateOnly(2025, 12, 1) ? 1000 : date.Year == 2026 ? 200 : 100)).ToArray();
var cyberForecast = EdiForecast.Build(cyberAsOf, cyberHistory);
Check(cyberForecast.Days[1].Parcels == 2000 && cyberForecast.Days[1].Annual?.Weight == 1,
    "Cyber Monday peak is scaled and never diluted by ordinary Mondays");
var missingCyber = EdiForecast.Build(cyberAsOf, cyberHistory.Where(d => d.Date != new DateOnly(2025, 12, 1)).ToArray());
Check(missingCyber.Days[1].Parcels == null, "Missing prior Cyber Monday is explicitly unavailable");
var futurePoisoned = EdiForecast.Build(cyberAsOf, cyberHistory.Concat(new[] { new EdiHistoryDay(cyberAsOf.AddDays(1), 9999999) }).ToArray());
Check(futurePoisoned.Total == cyberForecast.Total, "Annual calculations exclude future observations");
Check(annualForecast.Days.All(d => d.Annual!.References.All(r => r.Date < asOf)), "Annual references are always prior observations");
Check(annualForecast.Seasonality!.ComparedDays == annualForecast.BacktestDays
    && annualForecast.Seasonality.SeasonalComparedWape == annualForecast.BacktestWape, "Model comparison uses the same scored days");

var temp = Path.Combine(Path.GetTempPath(), "edi-archive-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
Environment.SetEnvironmentVariable("EDI_FORECAST_PATH", temp);
try
{
    var weekClock = new TestWeekClock(new DateTimeOffset(2026,9,12,10,0,0,TimeSpan.Zero)); // Saturday 6 am Montreal
    var weekSource = new EdiSectorForecastService(weekClock);
    var weekly = new EdiSectorWeeklyService(weekSource, new TestEnvironment(), weekClock);
    var saturday = new DateOnly(2026,9,12);
    var saturdayView = await weekly.GetAsync(saturday);
    Check(saturdayView.Weekly!.ForecastAvailable && weekSource.ForecastReads == 1, "Saturday creates weekly forecast once");
    Check(saturdayView.Sectors[0].Actuals!.All(a=>a.Parcels==null && a.Status=="future"), "Saturday does not expose future actuals as zero");
    var weeklyPath=Path.Combine(temp,"sectors-st-hubert","weekly-saturday","2026-09-12-forecast.json");
    var weeklyBytes=File.ReadAllBytes(weeklyPath);
    weekClock.Now=new DateTimeOffset(2026,9,14,9,59,0,TimeSpan.Zero);
    var beforeRefresh=await weekly.GetAsync(saturday.AddDays(2));
    Check(beforeRefresh.Sectors[0].Actuals![0].Parcels==null && weekSource.ActualReads==0, "Before 6 am Monday real remains pending");
    weekClock.Now=new DateTimeOffset(2026,9,14,10,0,0,TimeSpan.Zero);
    var mondayView=await weekly.GetAsync(saturday.AddDays(2));
    Check(mondayView.Sectors[0].Actuals![0].Parcels==125 && weekSource.ActualReads==1, "Monday 6 am updates Monday sorted cohort");
    weekSource.ActualVolume=150;
    await weekly.GetAsync(saturday.AddDays(2));
    Check(weekSource.ActualReads==1 && weekSource.ForecastReads==1, "Refreshing during day does not query actuals or recompute forecasts");
    weekly=new EdiSectorWeeklyService(weekSource,new TestEnvironment(),weekClock);
    var afterRestart=await weekly.GetAsync(saturday.AddDays(2));
    Check(afterRestart.Sectors[0].Actuals![0].Parcels==125 && weekSource.ActualReads==1, "Restart reloads daily actual snapshot unchanged");
    weekClock.Now=new DateTimeOffset(2026,9,15,10,0,0,TimeSpan.Zero);
    var tuesdayView=await weekly.GetAsync(saturday.AddDays(3));
    Check(tuesdayView.Sectors[0].Actuals![0].Parcels==150 && tuesdayView.Sectors[0].Actuals![1].Parcels==150, "Next morning updates real including late corrections");
    Check(weeklyBytes.SequenceEqual(File.ReadAllBytes(weeklyPath)) && weekSource.ForecastReads==1, "Weekly predictions never change when actuals change");
    var missedWeek=await weekly.GetAsync(saturday.AddDays(-6));
    Check(!missedWeek.Weekly!.ForecastAvailable && weekSource.ForecastReads==1, "Missing past Saturday never fabricates a retrospective forecast");
    Check(EdiSectorWeek.Saturday(new DateOnly(2026,9,18))==saturday && EdiSectorWeek.Saturday(saturday)==saturday, "Saturday through Friday select one fixed delivery week");
    Check(!EdiSectorWeek.CanCreate(saturday,new DateTime(2026,9,12,5,59,0)) && !EdiSectorWeek.CanCreate(saturday,new DateTime(2026,9,14,6,0,0)), "No early or retroactive Saturday creation");
    Check(EdiSectorWeek.Saturday(new DateOnly(2026,9,11)) == new DateOnly(2026,9,5) && EdiSectorWeek.Saturday(new DateOnly(2026,9,13)) == saturday, "Friday shows ending week; Saturday and Sunday show upcoming week");
    weekClock.Now = new DateTimeOffset(2026,9,19,9,59,0,TimeSpan.Zero);
    var earlySaturday = await weekly.GetAsync(new DateOnly(2026,9,19));
    Check(earlySaturday.Weekly!.WeekStart == new DateOnly(2026,9,21) && !earlySaturday.Weekly.ForecastAvailable, "Saturday before 6am displays next week without calculating early");
    weekClock.Now = new DateTimeOffset(2026,9,19,10,0,0,TimeSpan.Zero);
    var nextSaturday = await weekly.GetAsync(new DateOnly(2026,9,19));
    Check(nextSaturday.Weekly!.ForecastAvailable && weekSource.ForecastReads == 2, "New week calculated Saturday at 6am");
    var archive = new EdiForecastArchive(new TestEnvironment());
    var source = new ConveyorDataService();
    var first = await archive.EnsureCurrentAsync(source);
    var path = Path.Combine(temp, first.Id + ".json");
    var bytes = File.ReadAllBytes(path);
    var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => archive.EnsureCurrentAsync(source)));
    Check(source.Reads == 1 && results.All(s => s.Id == first.Id) && bytes.SequenceEqual(File.ReadAllBytes(path)), "Refresh is idempotent and archive immutable");
    var restarted = new EdiForecastArchive(new TestEnvironment());
    await restarted.EnsureCurrentAsync(source);
    Check(source.Reads == 1 && restarted.Read().Count == 1, "Restart reloads saved forecast without recalculation");
    var now = EdiForecastArchive.LocalNow;
    var today = DateOnly.FromDateTime(now.Hour < 4 ? now.AddDays(-1) : now);
    var target = today.AddDays(-2);
    var old = first with { Id = "prior-test", SavedAt = DateTimeOffset.UtcNow.AddDays(-5),
        Forecast = first.Forecast with { AsOfDate = target.AddDays(-1), Days = new[] {
            new EdiForecastDay(target, "test", 100, 80, 110, Array.Empty<EdiForecastSample>()) } } };
    var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
    File.WriteAllText(Path.Combine(temp, "prior-test.json"), System.Text.Json.JsonSerializer.Serialize(old, json));
    var late = old with { Id = "late-test", SavedAt = DateTimeOffset.UtcNow };
    File.WriteAllText(Path.Combine(temp, "late-test.json"), System.Text.Json.JsonSerializer.Serialize(late, json));
    var view = restarted.View(today, new[] { new EdiHistoryDay(target, 125) });
    var compared = view.Comparisons.Single(r => r.SnapshotId == "prior-test");
    Check(compared.Actual == 125 && compared.Difference == 25 && compared.ErrorPercent == 20, "Archived forecasts compared with completed actuals");
    Check(view.Comparisons.Single(r => r.SnapshotId == "late-test").Actual == null, "Late-created forecasts cannot be scored");
    var targetStart = target.ToDateTime(new TimeOnly(6, 0));
    var targetSaved = new DateTimeOffset(targetStart, TimeZoneInfo.FindSystemTimeZoneById("America/Toronto").GetUtcOffset(targetStart));
    var sameDay = old with { Id = "same-day-v4", ScheduledDate = target, SavedAt = targetSaved,
        Forecast = old.Forecast with { AsOfDate = target } };
    var legacySameDay = sameDay with { Id = "same-day-v3", ModelVersion = "weekday-annual-v3-cyber-monday" };
    var afterDay = sameDay with { Id = "after-day-v4", SavedAt = targetSaved.AddHours(22) };
    foreach (var snapshot in new[] { sameDay, legacySameDay, afterDay })
        File.WriteAllText(Path.Combine(temp, snapshot.Id + ".json"), System.Text.Json.JsonSerializer.Serialize(snapshot, json));
    var dayZeroView = restarted.View(today, new[] { new EdiHistoryDay(target, 125) });
    Check(dayZeroView.Comparisons.Single(r => r.SnapshotId == sameDay.Id).Actual == 125
        && dayZeroView.Comparisons.Single(r => r.SnapshotId == sameDay.Id).Horizon == 0, "6am day-zero forecast compares against completed actuals");
    Check(dayZeroView.Comparisons.Single(r => r.SnapshotId == legacySameDay.Id).Actual == null, "Legacy day eligibility stays unchanged");
    Check(dayZeroView.Comparisons.Single(r => r.SnapshotId == afterDay.Id).Actual == null, "No comparison for reconstruction after day closes");
    Check(bytes.SequenceEqual(File.ReadAllBytes(path)), "Comparison never rewrites prediction");
    var readsBeforeActuals = source.Reads;
    await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => archive.EnsureActualsAsync(source)));
    Check(source.Reads == readsBeforeActuals + 1, "Daily actuals are queried only once, including concurrent renewal requests");
    var actualFile = Path.Combine(temp, "actuals", EdiForecastArchive.DueDate(EdiForecastArchive.LocalNow).ToString("yyyy-MM-dd") + ".json");
    var actualBytes = File.ReadAllBytes(actualFile);
    await restarted.EnsureActualsAsync(source);
    var savedActuals = restarted.ReadActuals();
    for (var i = 0; i < 5; i++) restarted.View(today, savedActuals!.Days);
    Check(source.Reads == readsBeforeActuals + 1 && actualBytes.SequenceEqual(File.ReadAllBytes(actualFile)), "Archive-only page reads and restart never query the source or rewrite daily actuals");

}
finally
{
    Environment.SetEnvironmentVariable("EDI_FORECAST_PATH", null);
    Directory.Delete(temp, recursive: true);
}
Console.WriteLine("Forecast, holiday, scheduling, persistence and comparison checks passed.");
