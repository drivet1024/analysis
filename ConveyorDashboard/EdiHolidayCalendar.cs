// Operational exclusions, not a determination of employees' statutory entitlements.
static class EdiHolidayCalendar
{
    public static string? Name(DateOnly date)
    {
        var y = date.Year;
        var extra = (Environment.GetEnvironmentVariable("EDI_EXTRA_CLOSURES") ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (extra.Contains(date.ToString("yyyy-MM-dd"))) return "Fermeture supplémentaire";
        if (date == new DateOnly(y, 1, 1)) return "Jour de l’An";
        if (date == new DateOnly(y, 6, 24)) return "Fête nationale du Québec";
        if (date == new DateOnly(y, 7, 1)) return "Fête du Canada";
        if (date == new DateOnly(y, 12, 25)) return "Noël";
        if (date == new DateOnly(y, 6, 25) && date.DayOfWeek == DayOfWeek.Monday) return "Fête nationale (report)";
        if (date == new DateOnly(y, 7, 2) && date.DayOfWeek == DayOfWeek.Monday) return "Fête du Canada (report)";
        if (date.Month == 5 && date.DayOfWeek == DayOfWeek.Monday && date.Day is >= 18 and <= 24) return "Journée nationale des patriotes";
        if (date.Month == 9 && date.DayOfWeek == DayOfWeek.Monday && date.Day <= 7) return "Fête du Travail";
        if (date.Month == 10 && date.DayOfWeek == DayOfWeek.Monday && date.Day is >= 8 and <= 14) return "Action de grâce";
        // Gregorian Easter; both Easter closures excluded conservatively for network volumes.
        int a = y % 19, b = y / 100, c = y % 100, d = b / 4, e = b % 4,
            f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30,
            i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7,
            m = (a + 11 * h + 22 * l) / 451;
        var easter = new DateOnly(y, (h + l - 7 * m + 114) / 31, (h + l - 7 * m + 114) % 31 + 1);
        if (date == easter.AddDays(-2)) return "Vendredi saint";
        if (date == easter.AddDays(1)) return "Lundi de Pâques";
        return null;
    }
}
