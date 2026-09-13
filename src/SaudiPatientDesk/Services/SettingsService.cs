using System.IO;
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
}
