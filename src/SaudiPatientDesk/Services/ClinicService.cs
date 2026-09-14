using System.Globalization;
using Microsoft.Data.Sqlite;
using SaudiPatientDesk.Data;
using SaudiPatientDesk.Domain;

namespace SaudiPatientDesk.Services;

public sealed class ClinicService
{
    public const int MaxClinicsPerDay = 10;

    public int CountOpenOn(DateTime day)
    {
        var start = day.Date;
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(DISTINCT clinic_id) FROM appointments
            WHERE deleted_utc IS NULL AND status <> 'cancelled'
              AND starts_at_local >= $start AND starts_at_local < $end
              AND clinic_id IS NOT NULL;
            """;
        command.Parameters.AddWithValue("$start", FormatLocal(start));
        command.Parameters.AddWithValue("$end", FormatLocal(start.AddDays(1)));
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public string TodayUsageLabel() =>
        $"عيادات اليوم: {CountOpenOn(DateTime.Today)} / {MaxClinicsPerDay}";

    public IReadOnlyList<Clinic> ListActive()
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, full_name, is_active FROM clinics
            WHERE deleted_utc IS NULL AND is_active=1
            ORDER BY id;
            """;
        return Read(command);
    }

    public IReadOnlyList<Clinic> ListAll()
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, full_name, is_active FROM clinics
            WHERE deleted_utc IS NULL
            ORDER BY id;
            """;
        return Read(command);
    }

    public Clinic Add(string name)
    {
        var n = (name ?? "").Trim();
        if (n.Length < 2) throw new InvalidOperationException("أدخل اسم العيادة.");
        using var connection = Database.Open();
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM clinics WHERE deleted_utc IS NULL AND trim(full_name)=trim($n) LIMIT 1;";
            exists.Parameters.AddWithValue("$n", n);
            if (exists.ExecuteScalar() is not null)
                throw new InvalidOperationException("توجد عيادة بهذا الاسم مسبقاً.");
        }
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO clinics(full_name, is_active) VALUES($n,1); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$n", n);
        var id = (long)(command.ExecuteScalar() ?? 0L);
        Database.Log("clinic.add", n);
        return new Clinic(id, n, true);
    }

    public void SoftDelete(long id)
    {
        using var connection = Database.Open();
        using var future = connection.CreateCommand();
        future.CommandText = """
            SELECT COUNT(*) FROM appointments
            WHERE clinic_id=$id AND deleted_utc IS NULL AND status='scheduled'
              AND starts_at_local >= $now;
            """;
        future.Parameters.AddWithValue("$id", id);
        future.Parameters.AddWithValue("$now", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        if (Convert.ToInt32(future.ExecuteScalar()) > 0)
            throw new InvalidOperationException("لا يمكن حذف العيادة وفيها مواعيد قادمة. انقل المواعيد أو ألغها أولاً.");

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE clinics SET deleted_utc=$now, is_active=0 WHERE id=$id AND deleted_utc IS NULL;";
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("العيادة محذوفة مسبقاً أو غير موجودة.");
        Database.Log("clinic.delete", id.ToString(CultureInfo.InvariantCulture));
    }

    public static void EnsureClinicAllowedOnDay(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long clinicId,
        DateTime day,
        long? exceptAppointmentId = null)
    {
        var start = day.Date;

        using (var exists = connection.CreateCommand())
        {
            exists.Transaction = transaction;
            exists.CommandText = "SELECT 1 FROM clinics WHERE id=$id AND deleted_utc IS NULL AND is_active=1;";
            exists.Parameters.AddWithValue("$id", clinicId);
            if (exists.ExecuteScalar() is null)
                throw new InvalidOperationException("العيادة غير متاحة.");
        }

        using (var already = connection.CreateCommand())
        {
            already.Transaction = transaction;
            already.CommandText = """
                SELECT 1 FROM appointments
                WHERE clinic_id=$id AND deleted_utc IS NULL AND status <> 'cancelled'
                  AND starts_at_local >= $start AND starts_at_local < $end
                  AND ($except IS NULL OR id <> $except)
                LIMIT 1;
                """;
            already.Parameters.AddWithValue("$id", clinicId);
            already.Parameters.AddWithValue("$start", FormatLocal(start));
            already.Parameters.AddWithValue("$end", FormatLocal(start.AddDays(1)));
            already.Parameters.AddWithValue("$except", exceptAppointmentId is null ? DBNull.Value : exceptAppointmentId.Value);
            if (already.ExecuteScalar() is not null)
                return;
        }

        using var count = connection.CreateCommand();
        count.Transaction = transaction;
        count.CommandText = """
            SELECT COUNT(DISTINCT clinic_id) FROM appointments
            WHERE clinic_id IS NOT NULL AND deleted_utc IS NULL AND status <> 'cancelled'
              AND starts_at_local >= $start AND starts_at_local < $end
              AND ($except IS NULL OR id <> $except);
            """;
        count.Parameters.AddWithValue("$start", FormatLocal(start));
        count.Parameters.AddWithValue("$end", FormatLocal(start.AddDays(1)));
        count.Parameters.AddWithValue("$except", exceptAppointmentId is null ? DBNull.Value : exceptAppointmentId.Value);
        var used = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (used >= MaxClinicsPerDay)
            throw new InvalidOperationException($"لا يمكن تشغيل أكثر من {MaxClinicsPerDay} عيادات في نفس اليوم.");
    }

    public static void ReleaseUnusedClinicDays(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM clinic_open_days
            WHERE NOT EXISTS (
              SELECT 1 FROM appointments a
              WHERE a.clinic_id=clinic_open_days.clinic_id
                AND a.deleted_utc IS NULL AND a.status <> 'cancelled'
                AND a.starts_at_local >= clinic_open_days.open_date || ' 00:00'
                AND a.starts_at_local < date(clinic_open_days.open_date, '+1 day') || ' 00:00'
            );
            """;
        command.ExecuteNonQuery();
    }

    private static string FormatLocal(DateTime value) =>
        value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static List<Clinic> Read(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var list = new List<Clinic>();
        while (reader.Read())
            list.Add(new Clinic(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2) == 1));
        return list;
    }
}
