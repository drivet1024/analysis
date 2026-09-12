sealed record EdiGrowthPair(DateOnly Date, long Parcels, DateOnly PreviousDate, long PreviousParcels);
sealed record EdiAnnualAdjustment(DateOnly ReferenceDate, IReadOnlyList<EdiHistoryDay> References,
    double? GrowthFactor, int GrowthPairs, double? AdjustedParcels, double Weight, string? Event, string Note);
sealed record EdiSeasonalitySummary(DateOnly CyberMonday, DateOnly PreviousCyberMonday, long? PreviousCyberParcels,
    IReadOnlyList<EdiGrowthPair> GrowthPairs, double? BaselineMae, double? BaselineWape, int ComparedDays,
    double? SeasonalComparedMae, double? SeasonalComparedWape);

static class EdiSeasonality
{
    public static DateOnly CyberMonday(int year)
    {
        var november = new DateOnly(year, 11, 1);
        var thanksgiving = november.AddDays(((int)DayOfWeek.Thursday - (int)november.DayOfWeek + 7) % 7 + 21);
        return thanksgiving.AddDays(4);
    }

    public static int? EventOffset(DateOnly date, bool sectorDelivery = false)
    {
        if (sectorDelivery) date = EdiSectorCalendar.SortingAnchor(date);
        var offset = date.DayNumber - CyberMonday(date.Year).DayNumber;
        return offset is >= -7 and <= 13 ? offset : null;
    }

    public static DateOnly PreviousReference(DateOnly target, bool sectorDelivery = false)
    {
        if (EventOffset(target, sectorDelivery) is not int offset) return target.AddDays(-364);
        var reference = CyberMonday(target.Year - 1).AddDays(offset);
        return sectorDelivery ? reference.AddDays(reference.DayOfWeek == DayOfWeek.Friday ? 3 : 1) : reference;
    }
    private static string? Holiday(DateOnly date, bool sectorDelivery) => sectorDelivery ? EdiSectorCalendar.Holiday(date) : EdiHolidayCalendar.Name(date);

    public static EdiGrowthPair[] GrowthPairs(DateOnly cutoff, IReadOnlyDictionary<DateOnly, EdiHistoryDay> known, bool sectorDelivery = false)
    {
        // Matched days preserve weekdays and exclude closures and the commercial peak on both sides.
        return Enumerable.Range(1, 56).Select(i => cutoff.AddDays(-i))
            .Where(date => Holiday(date, sectorDelivery) == null && EventOffset(date, sectorDelivery) == null
                && Holiday(date.AddDays(-364), sectorDelivery) == null && EventOffset(date.AddDays(-364), sectorDelivery) == null
                && known.ContainsKey(date) && known.ContainsKey(date.AddDays(-364)))
            .Select(date => new EdiGrowthPair(date, known[date].Parcels, date.AddDays(-364), known[date.AddDays(-364)].Parcels))
            .OrderBy(pair => pair.Date).ToArray();
    }

    public static EdiAnnualAdjustment Adjust(DateOnly target, DateOnly cutoff,
        IReadOnlyDictionary<DateOnly, EdiHistoryDay> known, bool sectorDelivery = false)
    {
        var reference = PreviousReference(target, sectorDelivery);
        var offset = EventOffset(target, sectorDelivery);
        var eventName = offset == 0 ? "Cyber Monday" : offset == -3 ? "Black Friday"
            : offset.HasValue ? $"Période Cyber Monday (J{offset:+0;-0;0})" : null;
        if (sectorDelivery && eventName != null) eventName = $"Tri : {eventName} → livraison";
        var references = (offset.HasValue ? new[] { reference } : new[] { reference.AddDays(-7), reference, reference.AddDays(7) })
            .Where(date => date < cutoff && Holiday(date, sectorDelivery) == null
                && (offset.HasValue || EventOffset(date, sectorDelivery) == null) && known.ContainsKey(date))
            .Select(date => known[date]).ToArray();
        var pairs = GrowthPairs(cutoff, known, sectorDelivery);
        var previousTotal = pairs.Sum(pair => (double)pair.PreviousParcels);
        double? growth = pairs.Length >= 21 && previousTotal > 0 ? pairs.Sum(pair => (double)pair.Parcels) / previousTotal : null;
        double? adjusted = references.Length >= (offset.HasValue ? 1 : 2) && growth.HasValue
            ? references.Average(day => (double)day.Parcels) * growth.Value : null;
        return new(reference, references, growth, pairs.Length, adjusted, adjusted.HasValue ? (offset.HasValue ? 1 : 0.5) : 0,
            eventName, adjusted.HasValue
                ? offset.HasValue ? "Même position autour du Cyber Monday de l’an dernier, ajustée au niveau d’activité actuel."
                    : "Moyenne des jours comparables de l’an dernier (52 semaines auparavant, ± 7 jours), ajustée au niveau d’activité actuel."
                : "Référence annuelle insuffisante : au moins 21 paires récentes et une référence événementielle ou deux références ordinaires sont nécessaires.");
    }
}
