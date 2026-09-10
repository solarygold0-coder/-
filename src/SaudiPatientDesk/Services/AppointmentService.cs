using SaudiPatientDesk.Data;
using SaudiPatientDesk.Domain;

namespace SaudiPatientDesk.Services;

public sealed class AppointmentService
{
    private readonly PatientService _patients = new();

    public Appointment Add(int fileNumber, DateTime startsAt, string? notes)
    {
        var patient = _patients.FindByFileNumber(fileNumber)
            ?? throw new InvalidOperationException("رقم الملف غير موجود.");
        var error = Validation.Appointment(startsAt);
        if (error is not null) throw new InvalidOperationException(error);

        using var connection = Database.Open();
        using var closure = connection.CreateCommand();
        closure.CommandText = "SELECT reason FROM closure_dates WHERE closed_date=$date;";
        closure.Parameters.AddWithValue("$date", startsAt.ToString("yyyy-MM-dd"));
        if (closure.ExecuteScalar() is string reason)
            throw new InvalidOperationException("العيادة مغلقة في هذا اليوم: " + reason);

        using var conflict = connection.CreateCommand();
        conflict.CommandText = """
            SELECT COUNT(*) FROM appointments
            WHERE starts_at_local=$start AND deleted_utc IS NULL AND status <> 'cancelled';
            """;
        conflict.Parameters.AddWithValue("$start", startsAt.ToString("yyyy-MM-dd HH:mm"));
        if (Convert.ToInt32(conflict.ExecuteScalar()) > 0)
            throw new InvalidOperationException("هذا الوقت محجوز لمريض آخر.");

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO appointments(patient_id, starts_at_local, notes, created_utc, updated_utc)
            VALUES($patient, $start, $notes, $now, $now);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$patient", patient.Id);
        command.Parameters.AddWithValue("$start", startsAt.ToString("yyyy-MM-dd HH:mm"));
        command.Parameters.AddWithValue("$notes", (object?)notes?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        var id = (long)(command.ExecuteScalar() ?? 0L);
        return new Appointment(id, patient.Id, patient.FileNumber, patient.FullName, startsAt, "scheduled", notes);
    }

    public IReadOnlyList<Appointment> Upcoming(int days = 30)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id, p.id, p.file_number, p.full_name, a.starts_at_local, a.status, a.notes
            FROM appointments a JOIN patients p ON p.id=a.patient_id
            WHERE a.deleted_utc IS NULL AND p.deleted_utc IS NULL AND a.status='scheduled'
              AND a.starts_at_local >= $start AND a.starts_at_local < $end
            ORDER BY a.starts_at_local LIMIT 500;
            """;
        command.Parameters.AddWithValue("$start", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        command.Parameters.AddWithValue("$end", DateTime.Now.AddDays(days).ToString("yyyy-MM-dd HH:mm"));
        using var reader = command.ExecuteReader();
        var items = new List<Appointment>();
        while (reader.Read())
            items.Add(new Appointment(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2),
                reader.GetString(3), DateTime.Parse(reader.GetString(4)), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        return items;
    }

    public DashboardSnapshot Snapshot()
    {
        using var connection = Database.Open();
        static int Scalar(Microsoft.Data.Sqlite.SqliteConnection c, string sql)
        {
            using var command = c.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar());
        }
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var alertEnd = DateTime.Now.AddDays(2).ToString("yyyy-MM-dd HH:mm");
        var inactive = DateTime.UtcNow.AddYears(-10).ToString("O");
        return new DashboardSnapshot(
            Scalar(connection, "SELECT COUNT(*) FROM patients WHERE deleted_utc IS NULL;"),
            Scalar(connection, $"SELECT COUNT(*) FROM appointments WHERE deleted_utc IS NULL AND status='scheduled' AND starts_at_local LIKE '{today}%';"),
            Scalar(connection, $"SELECT COUNT(*) FROM appointments WHERE deleted_utc IS NULL AND status='scheduled' AND starts_at_local BETWEEN datetime('now','localtime') AND '{alertEnd}';"),
            Scalar(connection, $"SELECT COUNT(*) FROM patients WHERE deleted_utc IS NULL AND last_activity_utc < '{inactive}';"));
    }
}
