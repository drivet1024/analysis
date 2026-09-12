sealed record EdiSectorDefinition(int SectorId, string Name, int PostalCodes);
sealed record EdiSectorHistory(DateOnly Date, int SectorId, long Parcels, long PostalParcels, long RouteFallbackParcels, long ConflictingRouteParcels);
sealed record EdiSectorPrediction(int SectorId, string Name, int PostalCodes, long HistoricalParcels,
    long PostalParcels, long RouteFallbackParcels, long ConflictingRouteParcels, EdiForecastResponse Forecast,
    string? SectorContact = null, int? ActiveRoutes = null, IReadOnlyList<EdiSectorActual>? Actuals = null);
sealed record EdiSectorActual(DateOnly Date, long? Parcels, string Status);
sealed record EdiSectorWeeklyInfo(DateOnly WeekStart, DateOnly WeekEnd, bool ForecastAvailable,
    DateOnly ActualsAsOf, DateTimeOffset ActualsUpdatedAt, DateTimeOffset NextRefresh);
sealed record EdiSectorHistorySet(IReadOnlyList<EdiSectorHistory> Rows, IReadOnlyList<DateOnly> ObservedDates,
    long Total, long Outside, long Unmapped, long Ambiguous);
sealed record EdiSectorForecastResponse(DateOnly AsOfDate, DateTimeOffset SavedAt, bool Reconstructed,
    string ModelVersion, int DepotId, string DepotName, DateOnly HistoryStart, int ObservedNetworkDays,
    long NetworkParcels, long OutsideDepotParcels, long UnmappedParcels, long AmbiguousPostalParcels,
    IReadOnlyList<EdiSectorPrediction> Sectors, EdiSectorWeeklyInfo? Weekly = null);

static class EdiSectorWeek
{
    public static DateOnly Saturday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 1) % 7));
    public static bool CanCreate(DateOnly saturday, DateTime now) => DateOnly.FromDateTime(now) == saturday && now.Hour >= 6;
    public static EdiSectorActual Actual(DateOnly date, int sector, DateOnly due, EdiSectorHistorySet source)
    {
        if (date > due) return new(date, null, "future");
        if (!source.ObservedDates.Contains(date)) return new(date, null, "missing");
        return new(date, source.Rows.Where(r => r.Date == date && r.SectorId == sector).Sum(r => r.Parcels), "observed");
    }
}

static class EdiSectorModel
{
    public static EdiForecastResponse Build(DateOnly date, int sectorId, IReadOnlyList<EdiSectorHistory> rows,
        IReadOnlyList<DateOnly> observedNetworkDates)
    {
        var sectorRows = rows.Where(r => r.SectorId == sectorId).ToDictionary(r => r.Date, r => r.Parcels);
        if (sectorRows.Count == 0) return EdiForecast.Build(date, [], sectorDelivery: true);
        var first = sectorRows.Keys.Min();
        // Absence of this sector means zero only on a day observed in the source, after its first activity.
        var daily = observedNetworkDates.Where(d => d >= first && d < date && !EdiSectorCalendar.IsWeekend(d)).Distinct()
            .Select(d => new EdiHistoryDay(d, sectorRows.GetValueOrDefault(d))).ToArray();
        return EdiForecast.Build(date, daily, sectorDelivery: true);
    }
}


static class EdiSectorCalendar
{
    public static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public static DateOnly? DeliveryDate(DateTime scan)
    {
        if (scan.Hour is >= 3 and < 15) return null;
        var shift = DateOnly.FromDateTime(scan.AddHours(-3));
        if (shift.DayOfWeek == DayOfWeek.Saturday) return null;
        return shift.AddDays(shift.DayOfWeek == DayOfWeek.Friday ? 3 : 1);
    }
    // Monday combines Friday and Sunday; other delivery days follow the preceding evening.
    public static DateOnly SortingAnchor(DateOnly delivery) => delivery.AddDays(delivery.DayOfWeek == DayOfWeek.Monday ? -3 : -1);
    public static string? Holiday(DateOnly delivery)
    {
        if (EdiHolidayCalendar.Name(delivery) is string holiday) return holiday;
        var anchor = SortingAnchor(delivery);
        if (EdiHolidayCalendar.Name(anchor) is string prior) return $"Tri précédent : {prior}";
        return delivery.DayOfWeek == DayOfWeek.Monday && EdiHolidayCalendar.Name(delivery.AddDays(-1)) is string sunday
            ? $"Tri du dimanche : {sunday}" : null;
    }
}
