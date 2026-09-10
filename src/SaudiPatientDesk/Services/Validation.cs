using System.Text.RegularExpressions;

namespace SaudiPatientDesk.Services;

public static partial class Validation
{
    [GeneratedRegex("^[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsOnly();

    public static string? Patient(string fullName, string nationalId, string mobile)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 3)
            return "أدخل اسم المريض كاملاً.";
        if (nationalId.Length != 10 || !DigitsOnly().IsMatch(nationalId))
            return "رقم الهوية يجب أن يتكون من 10 أرقام إنجليزية فقط.";
        if (mobile.Length is < 9 or > 10 || !DigitsOnly().IsMatch(mobile))
            return "رقم الجوال يجب أن يتكون من 9 أو 10 أرقام إنجليزية فقط.";
        return null;
    }

    public static string? Appointment(DateTime value)
    {
        if (value.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
            return "لا يمكن حجز موعد يوم الجمعة أو السبت.";
        if (value <= DateTime.Now)
            return "يجب أن يكون الموعد في وقت لاحق.";
        return null;
    }
}
