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
        var date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM clinic_open_days WHERE open_date=$d;";
        command.Parameters.AddWithValue("$d", date);
        var marked = Convert.ToInt32(command.ExecuteScalar());
        if (marked > 0) return marked;
        using var fallback = connection.CreateCommand();
        fallback.CommandText = """
            SELECT COUNT(DISTINCT clinic_id) FROM appointments
            WHERE deleted_utc IS NULL AND status <> 'cancelled'
              AND starts_at_local LIKE $d AND clinic_id IS NOT NULL;
            """;
        fallback.Parameters.AddWithValue("$d", date + "%");
        return Convert.ToInt32(fallback.ExecuteScalar());
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
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO clinics(full_name, is_active) VALUES($n,1); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$n", n);
        var id = (long)(command.ExecuteScalar() ?? 0L);
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
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("العيادة محذوفة مسبقاً أو غير موجودة.");
    }

    public void EnsureClinicAllowedOnDay(long clinicId, DateTime day)
    {
        var date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var connection = Database.Open();

        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM clinics WHERE id=$id AND deleted_utc IS NULL AND is_active=1;";
            exists.Parameters.AddWithValue("$id", clinicId);
            if (exists.ExecuteScalar() is null)
                throw new InvalidOperationException("العيادة غير متاحة.");
        }

        using (var already = connection.CreateCommand())
        {
            already.CommandText = "SELECT 1 FROM clinic_open_days WHERE clinic_id=$id AND open_date=$d;";
            already.Parameters.AddWithValue("$id", clinicId);
            already.Parameters.AddWithValue("$d", date);
            if (already.ExecuteScalar() is not null)
                return;
        }

        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM clinic_open_days WHERE open_date=$d;";
        count.Parameters.AddWithValue("$d", date);
        var used = Convert.ToInt32(count.ExecuteScalar());
        if (used >= MaxClinicsPerDay)
            throw new InvalidOperationException($"لا يمكن تشغيل أكثر من {MaxClinicsPerDay} عيادات في نفس اليوم.");

        using var ins = connection.CreateCommand();
        ins.CommandText = "INSERT INTO clinic_open_days(clinic_id, open_date) VALUES($id,$d);";
        ins.Parameters.AddWithValue("$id", clinicId);
        ins.Parameters.AddWithValue("$d", date);
        try
        {
            ins.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("clinic_daily_limit", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"لا يمكن تشغيل أكثر من {MaxClinicsPerDay} عيادات في نفس اليوم.", ex);
        }
    }

    private static List<Clinic> Read(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var list = new List<Clinic>();
        while (reader.Read())
            list.Add(new Clinic(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2) == 1));
        return list;
    }
}
