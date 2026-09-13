using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.LightGbm;

sealed record EdiMlForecastDay(DateOnly Date, string DayName, long? Parcels, string Status);
sealed record EdiMlForecastResponse(string? ModelId, string FeatureVersion, DateTimeOffset? TrainedAt,
    DateOnly? TrainedThrough, int TrainingRows, double? BacktestMae, double? BacktestWape,
    double? StatisticalBacktestMae, double? StatisticalBacktestWape, int BacktestDays,
    IReadOnlyList<EdiMlForecastDay> Days, long? Total, string Status);
sealed record EdiMlModelMetadata(string Id, string FeatureVersion, DateTimeOffset TrainedAt,
    DateOnly TrainingStart, DateOnly TrainedThrough, int TrainingRows, double BacktestMae,
    double? BacktestWape, double? StatisticalBacktestMae, double? StatisticalBacktestWape, int BacktestDays);

sealed class EdiMlFeatureRow
{
    public float Label { get; set; }
    public float DayOfWeekSin { get; set; }
    public float DayOfWeekCos { get; set; }
    public float YearSin { get; set; }
    public float YearCos { get; set; }
    public float RecentFour { get; set; }
    public float RecentEight { get; set; }
    public float RecentTrend { get; set; }
    public float Lag7 { get; set; }
    public float Lag14 { get; set; }
    public float Lag21 { get; set; }
    public float Lag28 { get; set; }
    public float AnnualReference { get; set; }
    public float AnnualGrowth { get; set; }
    public float AnnualMissing { get; set; }
    public float EventOffset { get; set; }
    public float IsCyberPeriod { get; set; }
}

sealed class EdiMlScore
{
    [ColumnName("Score")]
    public float Score { get; set; }
}

sealed class EdiMlForecastService(IWebHostEnvironment environment, ILogger<EdiMlForecastService> logger)
{
    public const int TrainingHistoryDays = 1120;
    public const string FeatureVersion = "edi-calendar-lags-v1";
    private const int MinimumTrainingRows = 365;
    private const int ValidationDays = 56;
    private readonly string directory = Environment.GetEnvironmentVariable("EDI_ML_MODEL_PATH")
        ?? Path.Combine(environment.ContentRootPath, "App_Data", "ml-models", "edi");
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] FeatureColumns =
    [
        nameof(EdiMlFeatureRow.DayOfWeekSin), nameof(EdiMlFeatureRow.DayOfWeekCos),
        nameof(EdiMlFeatureRow.YearSin), nameof(EdiMlFeatureRow.YearCos),
        nameof(EdiMlFeatureRow.RecentFour), nameof(EdiMlFeatureRow.RecentEight),
        nameof(EdiMlFeatureRow.RecentTrend), nameof(EdiMlFeatureRow.Lag7),
        nameof(EdiMlFeatureRow.Lag14), nameof(EdiMlFeatureRow.Lag21),
        nameof(EdiMlFeatureRow.Lag28), nameof(EdiMlFeatureRow.AnnualReference),
        nameof(EdiMlFeatureRow.AnnualGrowth), nameof(EdiMlFeatureRow.AnnualMissing),
        nameof(EdiMlFeatureRow.EventOffset), nameof(EdiMlFeatureRow.IsCyberPeriod)
    ];

    public int RequiredHistoryDays(DateOnly asOf)
    {
        Directory.CreateDirectory(directory);
        return RequiresTraining(asOf, ReadLatestMetadata()) ? TrainingHistoryDays : EdiForecast.HistoryDays;
    }

    public async Task<EdiMlForecastResponse> BuildAsync(DateOnly asOf, IReadOnlyList<EdiHistoryDay> history,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(directory);
            var known = history.Where(day => day.Date < asOf).GroupBy(day => day.Date)
                .ToDictionary(group => group.Key, group => group.Last());
            var latest = ReadLatestMetadata();
            if (RequiresTraining(asOf, latest))
            {
                try { latest = TrainAndSave(asOf, known); }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Entraînement LightGBM EDI impossible; le dernier modèle valide sera conservé.");
                    latest = ReadLatestMetadata();
                }
            }

            if (latest == null)
                return Unavailable(asOf, "Historique insuffisant pour entraîner le premier modèle ML.NET.");

            var modelPath = Path.Combine(directory, latest.Id + ".zip");
            if (!File.Exists(modelPath))
                return Unavailable(asOf, "Fichier du modèle ML.NET indisponible.");

            var ml = CreateContext();
            var model = ml.Model.Load(modelPath, out _);
            var engine = ml.Model.CreatePredictionEngine<EdiMlFeatureRow, EdiMlScore>(model);
            var days = Enumerable.Range(0, 7).Select(offset =>
            {
                var target = asOf.AddDays(offset);
                if (EdiHolidayCalendar.Name(target) is string holiday)
                    return new EdiMlForecastDay(target, DayName(target), null, $"{holiday} : prévision ML suspendue");
                var features = CreateFeatures(target, known);
                if (features == null)
                    return new EdiMlForecastDay(target, DayName(target), null, "Historique comparable insuffisant");
                var score = engine.Predict(features).Score;
                var parcels = float.IsFinite(score) ? Math.Max(0L, (long)Math.Round(score, MidpointRounding.AwayFromZero)) : (long?)null;
                return new EdiMlForecastDay(target, DayName(target), parcels,
                    parcels.HasValue ? "Prévision LightGBM" : "Résultat ML.NET invalide");
            }).ToArray();
            return new(latest.Id, latest.FeatureVersion, latest.TrainedAt, latest.TrainedThrough,
                latest.TrainingRows, latest.BacktestMae, latest.BacktestWape,
                latest.StatisticalBacktestMae, latest.StatisticalBacktestWape, latest.BacktestDays, days,
                days.All(day => day.Parcels.HasValue) ? days.Sum(day => day.Parcels!.Value) : null,
                "Modèle LightGBM local chargé");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Prévision LightGBM EDI impossible; la prévision statistique reste disponible.");
            return Unavailable(asOf, "Prévision ML.NET temporairement indisponible.");
        }
        finally { gate.Release(); }
    }

    private EdiMlModelMetadata TrainAndSave(DateOnly asOf, IReadOnlyDictionary<DateOnly, EdiHistoryDay> known)
    {
        var rows = BuildRows(known).OrderBy(row => row.Date).ToArray();
        if (rows.Length < MinimumTrainingRows)
            throw new InvalidOperationException($"{rows.Length} journées ML utilisables; minimum {MinimumTrainingRows}.");
        var validationCount = Math.Min(ValidationDays, Math.Max(28, rows.Length / 5));
        var training = rows[..^validationCount];
        var validation = rows[^validationCount..];
        var ml = CreateContext();
        var validationModel = Pipeline(ml).Fit(ml.Data.LoadFromEnumerable(training.Select(row => row.Features)));
        var validationEngine = ml.Model.CreatePredictionEngine<EdiMlFeatureRow, EdiMlScore>(validationModel);
        double absoluteError = 0, statisticalAbsoluteError = 0, actualTotal = 0;
        var evaluated = 0;
        foreach (var row in validation)
        {
            var prediction = validationEngine.Predict(row.Features).Score;
            if (!float.IsFinite(prediction)) continue;
            var statistical = EdiForecast.Build(row.Date,
                known.Values.Where(day => day.Date < row.Date).ToArray()).Days[0].Parcels;
            if (!statistical.HasValue) continue;
            absoluteError += Math.Abs(prediction - row.Features.Label);
            statisticalAbsoluteError += Math.Abs(statistical.Value - row.Features.Label);
            actualTotal += row.Features.Label;
            evaluated++;
        }
        if (evaluated == 0) throw new InvalidOperationException("Aucune journée comparable pour valider LightGBM.");

        var allData = ml.Data.LoadFromEnumerable(rows.Select(row => row.Features));
        var finalModel = Pipeline(ml).Fit(allData);
        var id = $"edi-lightgbm-{asOf:yyyy-MM-dd}-v1";
        var modelPath = Path.Combine(directory, id + ".zip");
        var metadataPath = Path.Combine(directory, id + ".json");
        if (!File.Exists(modelPath))
        {
            var temporaryModel = modelPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                ml.Model.Save(finalModel, allData.Schema, temporaryModel);
                File.Move(temporaryModel, modelPath, overwrite: false);
            }
            finally { if (File.Exists(temporaryModel)) File.Delete(temporaryModel); }
        }
        var metadata = new EdiMlModelMetadata(id, FeatureVersion, DateTimeOffset.UtcNow,
            rows[0].Date, asOf.AddDays(-1), rows.Length,
            Math.Round(absoluteError / evaluated, 1),
            actualTotal > 0 ? Math.Round(absoluteError / actualTotal * 100, 1) : null,
            Math.Round(statisticalAbsoluteError / evaluated, 1),
            actualTotal > 0 ? Math.Round(statisticalAbsoluteError / actualTotal * 100, 1) : null, evaluated);
        if (!File.Exists(metadataPath))
        {
            var temporaryMetadata = metadataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryMetadata, JsonSerializer.Serialize(metadata, Json));
                File.Move(temporaryMetadata, metadataPath, overwrite: false);
            }
            finally { if (File.Exists(temporaryMetadata)) File.Delete(temporaryMetadata); }
        }
        return metadata;
    }

    private static IEstimator<ITransformer> Pipeline(MLContext ml) => ml.Transforms
        .Concatenate("Features", FeatureColumns)
        .Append(ml.Regression.Trainers.LightGbm(new LightGbmRegressionTrainer.Options
        {
            LabelColumnName = nameof(EdiMlFeatureRow.Label),
            FeatureColumnName = "Features",
            NumberOfLeaves = 8,
            MinimumExampleCountPerLeaf = 20,
            LearningRate = 0.03,
            NumberOfIterations = 250,
            NumberOfThreads = 2
        }));

    private static MLContext CreateContext() => new(seed: 1729);

    private EdiMlModelMetadata? ReadLatestMetadata()
    {
        if (!Directory.Exists(directory)) return null;
        return Directory.EnumerateFiles(directory, "edi-lightgbm-*.json")
            .Select(path =>
            {
                try { return JsonSerializer.Deserialize<EdiMlModelMetadata>(File.ReadAllText(path), Json); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Métadonnées LightGBM ignorées : {File}", Path.GetFileName(path));
                    return null;
                }
            })
            .Where(metadata => metadata != null && metadata.FeatureVersion == FeatureVersion
                && File.Exists(Path.Combine(directory, metadata.Id + ".zip")))
            .OrderByDescending(metadata => metadata!.TrainedThrough)
            .ThenByDescending(metadata => metadata!.TrainedAt)
            .FirstOrDefault();
    }

    private static (DateOnly Date, EdiMlFeatureRow Features)[] BuildRows(
        IReadOnlyDictionary<DateOnly, EdiHistoryDay> known)
    {
        if (known.Count == 0) return [];
        var first = known.Keys.Min().AddDays(364);
        return known.Keys.Where(date => date >= first && EdiHolidayCalendar.Name(date) == null)
            .OrderBy(date => date).Select(date => (Date: date, Features: CreateFeatures(date, known, known[date].Parcels)))
            .Where(row => row.Features != null).Select(row => (row.Date, row.Features!)).ToArray();
    }

    internal static EdiMlFeatureRow? CreateFeatures(DateOnly target,
        IReadOnlyDictionary<DateOnly, EdiHistoryDay> known, long label = 0)
    {
        if (EdiHolidayCalendar.Name(target) != null) return null;
        var recent = Enumerable.Range(1, 8).Select(week => target.AddDays(-7 * week))
            .Where(date => EdiHolidayCalendar.Name(date) == null && EdiSeasonality.EventOffset(date) == null
                && known.ContainsKey(date)).Select(date => (float)known[date].Parcels).ToArray();
        if (recent.Length < 4) return null;
        var recentFour = recent.Take(4).Average();
        var recentEight = recent.Average();
        float Lag(int days) => known.TryGetValue(target.AddDays(-days), out var day)
            && EdiHolidayCalendar.Name(day.Date) == null && EdiSeasonality.EventOffset(day.Date) == null
                ? day.Parcels : recentEight;
        var referenceDate = EdiSeasonality.PreviousReference(target);
        var annualMissing = !known.TryGetValue(referenceDate, out var annual)
            || EdiHolidayCalendar.Name(referenceDate) != null;
        var pairs = EdiSeasonality.GrowthPairs(target, known);
        var priorTotal = pairs.Sum(pair => (double)pair.PreviousParcels);
        var annualGrowth = pairs.Length >= 21 && priorTotal > 0
            ? (float)(pairs.Sum(pair => (double)pair.Parcels) / priorTotal) : 1f;
        var dayAngle = 2 * Math.PI * (int)target.DayOfWeek / 7;
        var yearAngle = 2 * Math.PI * (target.DayOfYear - 1) /
            (DateTime.IsLeapYear(target.Year) ? 366 : 365);
        var eventOffset = EdiSeasonality.EventOffset(target);
        return new EdiMlFeatureRow
        {
            Label = label,
            DayOfWeekSin = (float)Math.Sin(dayAngle), DayOfWeekCos = (float)Math.Cos(dayAngle),
            YearSin = (float)Math.Sin(yearAngle), YearCos = (float)Math.Cos(yearAngle),
            RecentFour = recentFour, RecentEight = recentEight,
            RecentTrend = recentEight > 0 ? recentFour / recentEight : 1,
            Lag7 = Lag(7), Lag14 = Lag(14), Lag21 = Lag(21), Lag28 = Lag(28),
            AnnualReference = annualMissing ? recentEight : annual!.Parcels,
            AnnualGrowth = annualGrowth, AnnualMissing = annualMissing ? 1 : 0,
            EventOffset = eventOffset ?? 30, IsCyberPeriod = eventOffset.HasValue ? 1 : 0
        };
    }

    private static DateOnly PreviousOrSameSaturday(DateOnly date) =>
        date.AddDays(-((7 + (int)date.DayOfWeek - (int)DayOfWeek.Saturday) % 7));

    private static bool RequiresTraining(DateOnly asOf, EdiMlModelMetadata? latest) => latest == null
        || (asOf.DayOfWeek == DayOfWeek.Saturday
            && latest.TrainedThrough < PreviousOrSameSaturday(asOf).AddDays(-1));

    private static string DayName(DateOnly date) => date.ToString("dddd", CultureInfo.GetCultureInfo("fr-CA"));

    private static EdiMlForecastResponse Unavailable(DateOnly asOf, string status)
    {
        var days = Enumerable.Range(0, 7).Select(offset =>
        {
            var date = asOf.AddDays(offset);
            return new EdiMlForecastDay(date, DayName(date), null, status);
        }).ToArray();
        return new(null, FeatureVersion, null, null, 0, null, null, null, null, 0, days, null, status);
    }
}
