using System.Text.RegularExpressions;

namespace SaudiPatientDesk.Services;

public static partial class Validation
{
    [GeneratedRegex("^[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsOnly();

    /// <summary>
    /// Sequential English file code: A-0001 … Z-9999, then AA-0001 …
    /// </summary>
    [GeneratedRegex("^[A-Z]{1,3}-[0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNumberPattern();

    public static string? FileNumber(string? fileNumber)
    {
        var value = NormalizeFileNumber(fileNumber ?? string.Empty);
        if (value.Length == 0)
            return "رقم الملف مطلوب.";
        if (!FileNumberPattern().IsMatch(value))
            return "رقم الملف يجب أن يكون بالشكل A-0001 (حرف إنجليزي ثم شرطة ثم 4 أرقام).";
        return null;
    }

    public static string NormalizeFileNumber(string fileNumber) =>
        fileNumber.Trim().ToUpperInvariant();

    public static string? Patient(string fullName, string nationalId, string mobile, string gender)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 3)
            return "أدخل اسم المريض كاملاً.";
        if (nationalId.Length != 10 || !DigitsOnly().IsMatch(nationalId))
            return "رقم الهوية يجب أن يتكون من 10 أرقام إنجليزية فقط.";
        if (mobile.Length is < 9 or > 10 || !DigitsOnly().IsMatch(mobile))
            return "رقم الجوال يجب أن يتكون من 9 أو 10 أرقام إنجليزية فقط.";
        if (gender is not ("male" or "female"))
            return "اختر جنس المريض (ذكر / أنثى).";
        return null;
    }

    public static string? Appointment(DateTime value, SettingsService settings, bool requireFuture = true)
    {
        if (settings.IsWeeklyClosed(value))
            return "لا يمكن الحجز في يوم الإغلاق الأسبوعي المحدد في الإعدادات.";
        if (requireFuture && value <= DateTime.Now)
            return "يجب أن يكون الموعد في وقت لاحق.";
        return null;
    }
}
