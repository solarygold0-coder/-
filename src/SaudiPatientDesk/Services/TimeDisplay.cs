using System.Globalization;

namespace SaudiPatientDesk.Services;

/// <summary>12-hour clock helpers for Arabic UI (ص = AM, م = PM). Storage remains 24h DateTime.</summary>
public static class TimeDisplay
{
    public static string Format12(DateTime dt) => Format12(dt.TimeOfDay);

    public static string Format12(TimeSpan t)
    {
        var totalHours = (int)t.TotalHours;
        if (totalHours is < 0 or >= 24) totalHours = ((totalHours % 24) + 24) % 24;
        var period = totalHours >= 12 ? "م" : "ص";
        var h12 = totalHours % 12;
        if (h12 == 0) h12 = 12;
        return $"{h12}:{t.Minutes:00} {period}";
    }

    public static string Format12Long(DateTime dt) =>
        dt.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) + "  " + Format12(dt);

    /// <summary>Convert 1–12 hour + ص/م to 0–23 hour.</summary>
    public static int ToHour24(int hour12, string period)
    {
        var isPm = period is "م" or "PM" or "pm" or "مساءً";
        if (hour12 is < 1 or > 12) hour12 = ((hour12 - 1) % 12) + 1;
        if (hour12 == 12) return isPm ? 12 : 0;
        return isPm ? hour12 + 12 : hour12;
    }

    public static (int Hour12, string Period) FromHour24(int hour24)
    {
        hour24 = ((hour24 % 24) + 24) % 24;
        var period = hour24 >= 12 ? "م" : "ص";
        var h12 = hour24 % 12;
        if (h12 == 0) h12 = 12;
        return (h12, period);
    }

    public static IReadOnlyList<string> PeriodOptions { get; } = new[] { "ص", "م" };

    public static IReadOnlyList<string> Hour12Options { get; } =
        Enumerable.Range(1, 12).Select(h => h.ToString(CultureInfo.InvariantCulture)).ToList();

    public static IReadOnlyList<string> MinuteOptions { get; } =
        Enumerable.Range(0, 60).Select(m => m.ToString("00", CultureInfo.InvariantCulture)).ToList();
}
