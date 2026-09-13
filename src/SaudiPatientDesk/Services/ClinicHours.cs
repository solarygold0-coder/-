using System.Globalization;

namespace SaudiPatientDesk.Services;

/// <summary>
/// Working hours and lunch break stored as HH:mm (24h) in app_settings.
/// Defaults: work 08:00–17:00, break 12:00–12:55, 15-minute slots.
/// UI presents times in 12-hour Arabic (ص/م).
/// </summary>
public sealed class ClinicHours
{
    public TimeSpan Start { get; }
    public TimeSpan End { get; }
    public TimeSpan BreakStart { get; }
    public TimeSpan BreakEnd { get; }
    public bool BreakEnabled { get; }
    public int SlotMinutes { get; }

    public ClinicHours(
        TimeSpan start,
        TimeSpan end,
        int slotMinutes = 15,
        TimeSpan? breakStart = null,
        TimeSpan? breakEnd = null,
        bool breakEnabled = true)
    {
        if (end <= start)
            throw new ArgumentException("يجب أن تكون نهاية الدوام بعد بدايته.");
        Start = start;
        End = end;
        SlotMinutes = slotMinutes is >= 5 and <= 60 ? slotMinutes : 15;
        BreakStart = breakStart ?? new TimeSpan(12, 0, 0);
        BreakEnd = breakEnd ?? new TimeSpan(12, 55, 0);
        BreakEnabled = breakEnabled && BreakEnd > BreakStart;
    }

    public static ClinicHours Load(SettingsService settings)
    {
        var start = ParseTime(settings.Get("work_start", "08:00"), new TimeSpan(8, 0, 0));
        var end = ParseTime(settings.Get("work_end", "17:00"), new TimeSpan(17, 0, 0));
        var slot = int.TryParse(settings.Get("slot_minutes", "15"), out var s) ? s : 15;
        var breakStart = ParseTime(settings.Get("break_start", "12:00"), new TimeSpan(12, 0, 0));
        var breakEnd = ParseTime(settings.Get("break_end", "12:55"), new TimeSpan(12, 55, 0));
        var breakOn = settings.Get("break_enabled", "1") is not ("0" or "false" or "off");
        if (end <= start)
            end = start.Add(TimeSpan.FromHours(8));
        return new ClinicHours(start, end, slot, breakStart, breakEnd, breakOn);
    }

    public void Save(SettingsService settings)
    {
        settings.Set("work_start", Format(Start));
        settings.Set("work_end", Format(End));
        settings.Set("slot_minutes", SlotMinutes.ToString(CultureInfo.InvariantCulture));
        settings.Set("break_start", Format(BreakStart));
        settings.Set("break_end", Format(BreakEnd));
        settings.Set("break_enabled", BreakEnabled ? "1" : "0");
    }

    public bool IsOnBreak(DateTime local)
    {
        if (!BreakEnabled) return false;
        var t = local.TimeOfDay;
        return t >= BreakStart && t < BreakEnd;
    }

    public bool IsWithinHours(DateTime local)
    {
        var t = local.TimeOfDay;
        if (t < Start || t >= End) return false;
        if (IsOnBreak(local)) return false;
        return true;
    }

    public string? ValidateAppointmentTime(DateTime local)
    {
        if (IsOnBreak(local))
            return $"لا يمكن الحجز أثناء الاستراحة ({TimeDisplay.Format12(BreakStart)} – {TimeDisplay.Format12(BreakEnd)}).";
        if (local.TimeOfDay < Start || local.TimeOfDay >= End)
            return $"وقت الموعد خارج الدوام ({TimeDisplay.Format12(Start)} – {TimeDisplay.Format12(End)}).";
        var elapsed = local.TimeOfDay - Start;
        if (elapsed.Ticks % TimeSpan.FromMinutes(SlotMinutes).Ticks != 0)
            return $"اختر وقتاً مطابقاً لفترات الحجز التي تبدأ كل {SlotMinutes} دقيقة من بداية الدوام.";
        return null;
    }

    public IReadOnlyList<TimeSpan> SlotStarts()
    {
        var slots = new List<TimeSpan>();
        for (var value = Start; value < End; value = value.Add(TimeSpan.FromMinutes(SlotMinutes)))
        {
            if (!BreakEnabled || value < BreakStart || value >= BreakEnd)
                slots.Add(value);
        }
        return slots;
    }

    public string BreakLabel =>
        BreakEnabled
            ? $"{TimeDisplay.Format12(BreakStart)} – {TimeDisplay.Format12(BreakEnd)}"
            : "غير مفعّلة";

    public static IReadOnlyList<string> FineMinuteOptions()
    {
        var list = new List<string>();
        for (var m = 0; m < 60; m += 5)
            list.Add(m.ToString("00", CultureInfo.InvariantCulture));
        return list;
    }

    public static string Format(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}";

    private static TimeSpan ParseTime(string? text, TimeSpan fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        if (TimeSpan.TryParseExact(text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var ts))
            return ts;
        if (TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out ts))
            return ts;
        return fallback;
    }
}
