using Microsoft.Data.Sqlite;

namespace SaudiPatientDesk.Data;

public static class Database
{
    public static SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = AppPaths.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    public static void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;

            CREATE TABLE IF NOT EXISTS schema_info (
                version INTEGER NOT NULL,
                created_utc TEXT NOT NULL
            );

            INSERT INTO schema_info(version, created_utc)
            SELECT 1, strftime('%Y-%m-%dT%H:%M:%fZ','now')
            WHERE NOT EXISTS (SELECT 1 FROM schema_info);

            CREATE TABLE IF NOT EXISTS patients (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_number INTEGER NOT NULL UNIQUE CHECK(file_number > 0),
                full_name TEXT NOT NULL CHECK(length(trim(full_name)) >= 3),
                national_id TEXT NOT NULL UNIQUE CHECK(national_id GLOB '[0-9]*' AND length(national_id) = 10),
                mobile TEXT NOT NULL CHECK(mobile GLOB '[0-9]*' AND length(mobile) BETWEEN 9 AND 10),
                secondary_contact TEXT,
                brief_medical_info TEXT,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                last_activity_utc TEXT NOT NULL,
                deleted_utc TEXT
            );

            CREATE INDEX IF NOT EXISTS ix_patients_name ON patients(full_name);
            CREATE INDEX IF NOT EXISTS ix_patients_mobile ON patients(mobile);

            CREATE TABLE IF NOT EXISTS appointments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE RESTRICT,
                starts_at_local TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'scheduled' CHECK(status IN ('scheduled','completed','cancelled','missed')),
                notes TEXT,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                deleted_utc TEXT
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_appointments_active_slot
            ON appointments(starts_at_local)
            WHERE deleted_utc IS NULL AND status <> 'cancelled';

            CREATE INDEX IF NOT EXISTS ix_appointments_patient ON appointments(patient_id, starts_at_local);

            CREATE TABLE IF NOT EXISTS attachments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE RESTRICT,
                stored_name TEXT NOT NULL UNIQUE,
                original_name TEXT NOT NULL,
                content_type TEXT,
                size_bytes INTEGER NOT NULL CHECK(size_bytes >= 0),
                created_utc TEXT NOT NULL,
                deleted_utc TEXT
            );

            CREATE TABLE IF NOT EXISTS closure_dates (
                closed_date TEXT PRIMARY KEY,
                reason TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}
