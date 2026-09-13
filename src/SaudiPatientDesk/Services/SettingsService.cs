using System.IO;
using System.Globalization;
using Microsoft.Data.Sqlite;
using SaudiPatientDesk.Data;

namespace SaudiPatientDesk.Services;

public sealed class SettingsService
{
    public string DataLocation => AppPaths.Root;

    public string Get(string key, string fallback = "")
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT setting_value FROM app_settings WHERE setting_key=$key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string ?? fallback;
    }

    public void Set(string key, string value)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings(setting_key, setting_value, updated_utc)
            VALUES($key, $value, $now)
            ON CONFLICT(setting_key) DO UPDATE SET
              setting_value=excluded.setting_value,
              updated_utc=excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value.Trim());
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public string CreateBackup()
    {
        AppPaths.EnsureCreated();
        var target = Path.Combine(AppPaths.Backups, $"patient-records-{DateTime.Now:yyyyMMdd-HHmmss}.sqlite3");
        using var source = Database.Open();
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
        return target;
    }

    public IReadOnlyList<ClosureDay> ListClosures()
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT closed_date, reason FROM closure_dates ORDER BY closed_date;";
        using var reader = command.ExecuteReader();
        var rows = new List<ClosureDay>();
        while (reader.Read())
        {
            var day = DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            rows.Add(new ClosureDay(day, reader.GetString(1)));
        }
        return rows;
    }

    public void AddClosure(DateOnly day, string reason)
    {
        var normalizedReason = (reason ?? string.Empty).Trim();
        if (normalizedReason.Length < 2)
            throw new InvalidOperationException("أدخل سبب الإغلاق.");
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO closure_dates(closed_date, reason) VALUES($date, $reason)
            ON CONFLICT(closed_date) DO UPDATE SET reason=excluded.reason;
            """;
        command.Parameters.AddWithValue("$date", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$reason", normalizedReason);
        command.ExecuteNonQuery();
        Database.Log("closure.save", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    public void RemoveClosure(DateOnly day)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM closure_dates WHERE closed_date=$date;";
        command.Parameters.AddWithValue("$date", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("يوم الإغلاق غير موجود.");
        Database.Log("closure.delete", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}

public sealed record ClosureDay(DateOnly Day, string Reason)
{
    public string Display => $"{Day:yyyy-MM-dd} — {Reason}";
}
