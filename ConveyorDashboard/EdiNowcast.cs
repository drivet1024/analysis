sealed record EdiIntradaySample(DateOnly Date, long ParcelsAtSameTime, long FinalParcels, int Weight);
sealed record EdiNowcastResponse(DateTime AsOf, string Status, long Created, long? EstimatedFinal,
    long? Remaining, double? HistoricalProgressPercent, IReadOnlyList<EdiIntradaySample> Samples, string Explanation);

static class EdiNowcast
{
    public static EdiNowcastResponse Build(DateOnly date, DateTime asOf, long created,
        IReadOnlyList<EdiIntradaySample> history, bool completed = false)
    {
        if (completed) return new(asOf, "completed", created, created, 0, 100, [], "Journée terminée : total réel observé, et non une prévision.");
        var samples = history.Where(d => d.Date < date && d.Date >= date.AddDays(-56)
                && d.Date.DayOfWeek == date.DayOfWeek && EdiHolidayCalendar.Name(d.Date) == null
                && EdiSeasonality.EventOffset(d.Date) == null && d.FinalParcels > 0
                && d.ParcelsAtSameTime >= 0 && d.ParcelsAtSameTime <= d.FinalParcels)
            .OrderBy(d => d.Date).Select((d, i) => d with { Weight = i + 1 }).ToArray();
        EdiNowcastResponse Unavailable(string status, string message, double? progress = null) =>
            new(asOf, status, created, null, null, progress, samples, message);
        if (EdiHolidayCalendar.Name(date) != null || EdiSeasonality.EventOffset(date) != null)
            return Unavailable("special-day", "Journée fériée ou période Cyber Monday : les profils des journées ordinaires ne sont pas comparables.");
        if (samples.Length < 4)
            return Unavailable("insufficient-history", "Au moins quatre journées similaires complètes sont nécessaires pour estimer la finale.");
        var weightedFinal = samples.Sum(d => (double)d.FinalParcels * d.Weight);
        var fraction = samples.Sum(d => (double)d.ParcelsAtSameTime * d.Weight) / weightedFinal;
        if (created == 0 || asOf < date.ToDateTime(new TimeOnly(5, 0)) || fraction < 0.05)
            return Unavailable("too-early", "Estimation en attente : il faut des colis créés, au moins une heure écoulée et 5 % du volume historique habituel déjà reçu.", fraction * 100);
        var estimate = Math.Max(created, (long)Math.Round(created / fraction, MidpointRounding.AwayFromZero));
        return new(asOf, "estimated", created, estimate, estimate - created, fraction * 100, samples,
            "Colis déjà créés ÷ proportion historiquement reçue à la même heure, pondérée en faveur des journées récentes. Estimation indicative : un changement d’horaire des EDI peut la faire varier.");
    }
}
