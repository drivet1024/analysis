using System.Globalization;

sealed record EdiHistoryDay(DateOnly Date, long Parcels);
sealed record EdiForecastSample(DateOnly Date, long Parcels, int Weight);
sealed record EdiForecastDay(DateOnly Date, string DayName, long? Parcels,
    long? HistoricalLow, long? HistoricalHigh, IReadOnlyList<EdiForecastSample> Samples, string? Holiday = null,
    long? RecentEstimate = null, EdiAnnualAdjustment? Annual = null);
sealed record EdiForecastResponse(DateOnly AsOfDate, DateOnly HistoryStart, DateOnly HistoryEnd,
    int ObservedDays, int MissingDays, IReadOnlyList<EdiForecastDay> Days,
    long? Total, double? BacktestMae, double? BacktestWape, int BacktestDays,
    IReadOnlyList<EdiHistoryDay>? ExcludedHolidays = null, EdiSeasonalitySummary? Seasonality = null);

static class EdiForecast
{
    public const int HistoryDays = 455;
    // A missing source day is unknown, never silently converted to zero.
    public static EdiForecastResponse Build(DateOnly asOf, IReadOnlyList<EdiHistoryDay> history, bool sectorDelivery = false)
    {
        string? Holiday(DateOnly d) => sectorDelivery ? EdiSectorCalendar.Holiday(d) : EdiHolidayCalendar.Name(d);
        var start = asOf.AddDays(-HistoryDays);
        var known = history.Where(d => d.Date >= start && d.Date < asOf).ToDictionary(d => d.Date);
        EdiForecastDay Predict(DateOnly target, DateOnly cutoff, bool seasonal = true)
        {
            if (sectorDelivery && EdiSectorCalendar.IsWeekend(target))
                return new(target, target.ToString("dddd", CultureInfo.GetCultureInfo("fr-CA")), 0, 0, 0, [], "Aucune livraison le week-end", 0);
            var samples = known.Values.Where(d => d.Date < cutoff && d.Date >= cutoff.AddDays(-56)
                    && Holiday(d.Date) == null
                    && (!seasonal || EdiSeasonality.EventOffset(d.Date, sectorDelivery) == null)
                    && d.Date.DayOfWeek == target.DayOfWeek)
                .OrderBy(d => d.Date).Select((d, i) => new EdiForecastSample(d.Date, d.Parcels, i + 1)).ToArray();
            var holiday = Holiday(target);
            long? estimate = samples.Length < 4 || holiday != null ? null : (long)Math.Round(
                samples.Sum(d => (double)d.Parcels * d.Weight) / samples.Sum(d => d.Weight), MidpointRounding.AwayFromZero);
            var recent = estimate;
            var annual = seasonal ? EdiSeasonality.Adjust(target, cutoff, known, sectorDelivery) : null;
            if (seasonal && holiday == null)
            {
                if (annual!.AdjustedParcels is double adjusted && (recent.HasValue || annual.Event != null))
                    estimate = (long)Math.Round(annual.Weight * adjusted + (1 - annual.Weight) * (recent ?? 0), MidpointRounding.AwayFromZero);
                else if (annual.Event != null) estimate = null;
            }
            return new(target, target.ToString("dddd", CultureInfo.GetCultureInfo("fr-CA")), estimate,
                samples.Length == 0 ? null : samples.Min(d => d.Parcels),
                samples.Length == 0 ? null : samples.Max(d => d.Parcels), samples, holiday, recent, annual);
        }

        var firstOffset = sectorDelivery ? 1 : 0;
        var days = Enumerable.Range(firstOffset, 7).Select(offset => Predict(asOf.AddDays(offset), asOf)).ToArray();
        double error = 0, actual = 0, baselineError = 0, comparedActual = 0, seasonalComparedError = 0;
        var compared = 0;
        var tested = 0;
        // Four historical horizons with the same start offset as the live forecast.
        for (var week = 0; week < 4; week++)
        {
            var cutoff = asOf.AddDays(-28 - firstOffset + 7 * week);
            for (var offset = firstOffset; offset < firstOffset + 7; offset++)
            {
                var prediction = Predict(cutoff.AddDays(offset), cutoff);
                if (prediction.Parcels is not long value || !known.TryGetValue(prediction.Date, out var observed)) continue;
                error += Math.Abs((double)value - observed.Parcels);
                actual += observed.Parcels;
                tested++;
                var baseline = Predict(prediction.Date, cutoff, seasonal: false);
                if (baseline.Parcels is long baselineValue)
                {
                    baselineError += Math.Abs((double)baselineValue - observed.Parcels);
                    seasonalComparedError += Math.Abs((double)value - observed.Parcels);
                    comparedActual += observed.Parcels;
                    compared++;
                }
            }
        }
        var cyber = EdiSeasonality.CyberMonday(asOf.Year);
        if (cyber < asOf) cyber = EdiSeasonality.CyberMonday(asOf.Year + 1);
        var previousCyber = EdiSeasonality.CyberMonday(cyber.Year - 1);
        return new(asOf, start, asOf.AddDays(-1), known.Count, (sectorDelivery ? Enumerable.Range(1, HistoryDays).Count(i => !EdiSectorCalendar.IsWeekend(asOf.AddDays(-i))) : HistoryDays) - known.Count, days,
            days.All(d => d.Parcels.HasValue) ? days.Sum(d => d.Parcels!.Value) : null,
            tested > 0 ? Math.Round(error / tested, 1) : null,
            actual > 0 ? Math.Round(error / actual * 100, 1) : null, tested,
            known.Values.Where(d => Holiday(d.Date) != null).OrderBy(d => d.Date).ToArray(),
            new(cyber, previousCyber, known.TryGetValue(previousCyber, out var previous) ? previous.Parcels : null,
                EdiSeasonality.GrowthPairs(asOf, known, sectorDelivery), compared > 0 ? Math.Round(baselineError / compared, 1) : null,
                comparedActual > 0 ? Math.Round(baselineError / comparedActual * 100, 1) : null, compared,
                compared > 0 ? Math.Round(seasonalComparedError / compared, 1) : null,
                comparedActual > 0 ? Math.Round(seasonalComparedError / comparedActual * 100, 1) : null));
    }
}
