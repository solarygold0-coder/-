using System.Globalization;
using Microsoft.Data.Sqlite;
using SaudiPatientDesk.Data;
using SaudiPatientDesk.Domain;

namespace SaudiPatientDesk.Services;

public sealed record PatientSafetyFlags(
    bool HasDrugAllergy,
    string? AllergyText,
    bool HasChronic,
    string? ChronicText,
    string? BloodType,
    bool HasAnyAlert)
{
    public static PatientSafetyFlags From(Patient p)
    {
        var allergy = !string.IsNullOrWhiteSpace(p.DrugAllergies);
        var chronic = !string.IsNullOrWhiteSpace(p.ChronicDiseases);
        return new PatientSafetyFlags(
            allergy,
            p.DrugAllergies?.Trim(),
            chronic,
            p.ChronicDiseases?.Trim(),
            p.BloodType,
            allergy || chronic);
    }

    public string ArabicBannerText
    {
        get
        {
            var parts = new List<string>();
            if (HasDrugAllergy)
                parts.Add("تحسس أدوية: " + AllergyText);
            if (HasChronic)
                parts.Add("أمراض مزمنة: " + ChronicText);
            if (!string.IsNullOrWhiteSpace(BloodType))
                parts.Add("فصيلة الدم: " + BloodType);
            return string.Join("  |  ", parts);
        }
    }
}

public sealed record NoShowHint(double MissedRatio, int MissedCount, int SampleSize, string MessageAr);

public sealed class SmartAssistService
{
    private readonly SettingsService _settings = new();

    /// <summary>Safety flags from stored patient medical fields (no inference).</summary>
    public PatientSafetyFlags GetSafetyFlags(Patient patient) => PatientSafetyFlags.From(patient);

    /// <summary>
    /// Earliest open slot at or after <paramref name="from"/>, using the configured clinic grid.
    /// Skips Friday/Saturday, custom closure_dates, and occupied non-cancelled slots.
    /// </summary>
    public DateTime? SuggestNextOpenSlot(
        DateTime from,
        int maxDays = 14,
        long? doctorId = null,
        long? specialistId = null,
        long? exceptAppointmentId = null)
    {
        var hours = ClinicHours.Load(_settings);
        using var connection = Database.Open();
        var threshold = from <= DateTime.Now ? DateTime.Now : from;
        var firstDate = DateOnly.FromDateTime(threshold);
        for (var dayOffset = 0; dayOffset <= maxDays; dayOffset++)
        {
            var date = firstDate.AddDays(dayOffset);
            var day = date.ToDateTime(TimeOnly.MinValue);
            if (IsWeeklyClosed(day) || IsCustomClosed(connection, day)) continue;
            foreach (var slot in hours.SlotStarts())
            {
                var candidate = date.ToDateTime(TimeOnly.FromTimeSpan(slot));
                if (candidate > threshold &&
                    !IsSlotTaken(connection, candidate, doctorId, specialistId, exceptAppointmentId))
                    return candidate;
            }
        }
        return null;
    }

    /// <summary>Simple no-show ratio from past completed/missed appointments.</summary>
    public NoShowHint? GetNoShowHint(long patientId)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, COUNT(*) FROM appointments
            WHERE patient_id=$id AND deleted_utc IS NULL
              AND status IN ('completed','missed')
            GROUP BY status;
            """;
        command.Parameters.AddWithValue("$id", patientId);

        var completed = 0;
        var missed = 0;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var status = reader.GetString(0);
                var count = reader.GetInt32(1);
                if (status == "completed") completed = count;
                else if (status == "missed") missed = count;
            }
        }

        var sample = completed + missed;
        if (sample < 3) return null;
        var ratio = missed / (double)sample;
        if (ratio < 0.4) return null;

        return new NoShowHint(
            ratio,
            missed,
            sample,
            $"تنبيه: هذا المراجع تغيّب عن {missed} من أصل {sample} مواعيد سابقة.");
    }

    /// <summary>Short insight strings for the dashboard (local SQL only).</summary>
    public IReadOnlyList<string> DashboardInsights()
    {
        var lines = new List<string>();
        using var connection = Database.Open();

        // Peak hour among scheduled appointments in last 30 days
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT substr(starts_at_local, 12, 2) AS hh, COUNT(*) AS c
                FROM appointments
                WHERE deleted_utc IS NULL
                  AND starts_at_local >= $from
                GROUP BY hh
                ORDER BY c DESC
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$from", DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var hour = reader.GetString(0);
                var count = reader.GetInt32(1);
                lines.Add($"ساعة الذروة (آخر 30 يوماً): {hour}:00 — {count} موعد");
            }
        }

        // Missed in last 30 days
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COUNT(*) FROM appointments
                WHERE deleted_utc IS NULL AND status='missed'
                  AND starts_at_local >= $from;
                """;
            cmd.Parameters.AddWithValue("$from", DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            var missed = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            if (missed > 0)
                lines.Add($"مواعيد لم يحضر (آخر 30 يوماً): {missed}");
        }

        // Patients with drug allergies on file
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COUNT(*) FROM patients
                WHERE deleted_utc IS NULL
                  AND drug_allergies IS NOT NULL AND length(trim(drug_allergies)) > 0;
                """;
            var n = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            if (n > 0)
                lines.Add($"ملفات فيها تحسس أدوية مسجّل: {n}");
        }

        if (lines.Count == 0)
            lines.Add("لا توجد رؤى إضافية بعد — ستظهر مع تراكم المواعيد.");

        return lines;
    }

    private static bool IsWeeklyClosed(DateTime date) =>
        date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;

    private static bool IsCustomClosed(SqliteConnection connection, DateTime date)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM closure_dates WHERE closed_date=$d LIMIT 1;";
        command.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return command.ExecuteScalar() is not null;
    }

    private static bool IsSlotTaken(
        SqliteConnection connection,
        DateTime startsAt,
        long? doctorId,
        long? specialistId,
        long? exceptAppointmentId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM appointments
            WHERE deleted_utc IS NULL AND status <> 'cancelled'
              AND starts_at_local=$start
              AND ($except IS NULL OR id <> $except)
              AND (
                    ($doctor IS NOT NULL AND (doctor_id=$doctor OR staff_id=$doctor))
                 OR ($specialist IS NOT NULL AND (specialist_id=$specialist OR staff_id=$specialist))
                 OR ($doctor IS NULL AND $specialist IS NULL)
              )
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$start",
            startsAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$except", exceptAppointmentId is null ? DBNull.Value : exceptAppointmentId.Value);
        command.Parameters.AddWithValue("$doctor", doctorId is null ? DBNull.Value : doctorId.Value);
        command.Parameters.AddWithValue("$specialist", specialistId is null ? DBNull.Value : specialistId.Value);
        return command.ExecuteScalar() is not null;
    }
}
