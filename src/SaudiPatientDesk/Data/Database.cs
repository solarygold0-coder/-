using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;

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

    public static void Log(string action, string detail)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO audit_log(at_utc, action, detail) VALUES($t,$a,$d);";
        command.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$a", action);
        command.Parameters.AddWithValue("$d", detail ?? "");
        command.ExecuteNonQuery();
    }

    public static IReadOnlyList<(string At, string Action, string Detail)> RecentLog(int limit = 50)
    {
        var rows = new List<(string, string, string)>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT at_utc, action, detail FROM audit_log ORDER BY id DESC LIMIT $n;";
        command.Parameters.AddWithValue("$n", limit);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return rows;
    }

    public static void BackupNow()
    {
        try
        {
            AppPaths.EnsureCreated();
            if (!File.Exists(AppPaths.DatabaseFile)) return;
            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                command.ExecuteNonQuery();
            }
            var name = "auto-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".sqlite3";
            File.Copy(AppPaths.DatabaseFile, Path.Combine(AppPaths.Backups, name), overwrite: true);
            var old = Directory.GetFiles(AppPaths.Backups, "auto-*.sqlite3")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(7);
            foreach (var file in old)
            {
                try { file.Delete(); } catch { }
            }
        }
        catch
        {
        }
    }

    public static void Initialize()
    {
        InitializeCore();
        BackupNow();
    }

    private static void InitializeCore()
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
            SELECT 5, strftime('%Y-%m-%dT%H:%M:%fZ','now')
            WHERE NOT EXISTS (SELECT 1 FROM schema_info);

            CREATE TABLE IF NOT EXISTS app_settings (
                setting_key TEXT PRIMARY KEY,
                setting_value TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS audit_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                at_utc TEXT NOT NULL,
                action TEXT NOT NULL,
                detail TEXT NOT NULL
            );

            -- file_number: sequential English code A-0001 … Z-9999, then AA-0001 …
            CREATE TABLE IF NOT EXISTS patients (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_number TEXT NOT NULL UNIQUE
                    CHECK(file_number GLOB '[A-Z]*-[0-9][0-9][0-9][0-9]'),
                full_name TEXT NOT NULL CHECK(length(trim(full_name)) >= 3),
                national_id TEXT NOT NULL
                    CHECK(national_id NOT GLOB '*[^0-9]*' AND length(national_id) = 10),
                mobile TEXT NOT NULL
                    CHECK(mobile NOT GLOB '*[^0-9]*' AND length(mobile) BETWEEN 9 AND 10),
                secondary_contact TEXT,
                brief_medical_info TEXT,
                gender TEXT NOT NULL DEFAULT 'male'
                    CHECK(gender IN ('male','female')),
                residency_number TEXT,
                blood_type TEXT,
                chronic_diseases TEXT,
                current_medications TEXT,
                drug_allergies TEXT,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                last_activity_utc TEXT NOT NULL,
                deleted_utc TEXT
            );

            CREATE INDEX IF NOT EXISTS ix_patients_national
                ON patients(national_id);

            CREATE INDEX IF NOT EXISTS ix_patients_name ON patients(full_name);
            CREATE INDEX IF NOT EXISTS ix_patients_mobile ON patients(mobile);

            CREATE TABLE IF NOT EXISTS appointments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                patient_id INTEGER NOT NULL REFERENCES patients(id) ON DELETE RESTRICT,
                starts_at_local TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'scheduled'
                    CHECK(status IN ('scheduled','completed','cancelled','missed')),
                notes TEXT,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                deleted_utc TEXT
            );

            CREATE INDEX IF NOT EXISTS ix_appointments_slot
                ON appointments(starts_at_local);

            CREATE INDEX IF NOT EXISTS ix_appointments_patient
                ON appointments(patient_id, starts_at_local);

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

            CREATE TABLE IF NOT EXISTS staff (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                full_name TEXT NOT NULL CHECK(length(trim(full_name)) >= 3),
                role TEXT NOT NULL CHECK(role IN ('doctor','specialist')),
                is_active INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS clinics (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                full_name TEXT NOT NULL CHECK(length(trim(full_name)) >= 2),
                is_active INTEGER NOT NULL DEFAULT 1,
                deleted_utc TEXT
            );

            CREATE TABLE IF NOT EXISTS clinic_open_days (
                clinic_id INTEGER NOT NULL REFERENCES clinics(id) ON DELETE RESTRICT,
                open_date TEXT NOT NULL,
                PRIMARY KEY (clinic_id, open_date)
            );

            CREATE TRIGGER IF NOT EXISTS trg_clinic_daily_limit
            BEFORE INSERT ON clinic_open_days
            WHEN (SELECT COUNT(*) FROM clinic_open_days WHERE open_date=NEW.open_date) >= 10
            BEGIN
                SELECT RAISE(ABORT, 'clinic_daily_limit');
            END;
            """;
        command.ExecuteNonQuery();

        // Migrate older DBs that lack the new medical/demographic columns.
        EnsureColumn(connection, "patients", "gender", "TEXT NOT NULL DEFAULT 'male'");
        EnsureColumn(connection, "patients", "residency_number", "TEXT");
        EnsureColumn(connection, "patients", "blood_type", "TEXT");
        EnsureColumn(connection, "patients", "chronic_diseases", "TEXT");
        EnsureColumn(connection, "patients", "current_medications", "TEXT");
        EnsureColumn(connection, "patients", "drug_allergies", "TEXT");
        MigratePatientFileNumbersToCodes(connection);

        SeedSetting(connection, "work_start", "08:00");
        SeedSetting(connection, "work_end", "17:00");
        SeedSetting(connection, "slot_minutes", "15");
        SeedSetting(connection, "break_start", "12:00");
        SeedSetting(connection, "break_end", "12:55");
        SeedSetting(connection, "break_enabled", "1");
        SeedSetting(connection, "ui_language", "ar");

        EnsureColumn(connection, "appointments", "staff_id", "INTEGER");
        EnsureColumn(connection, "appointments", "doctor_id", "INTEGER");
        EnsureColumn(connection, "appointments", "specialist_id", "INTEGER");
        EnsureColumn(connection, "appointments", "clinic_id", "INTEGER");
        EnsureColumn(connection, "appointments", "kind", "TEXT NOT NULL DEFAULT 'regular'");
        EnsureColumn(connection, "appointments", "visit_stage", "TEXT NOT NULL DEFAULT ''");
        using (var drop = connection.CreateCommand())
        {
            drop.CommandText = """
                DROP INDEX IF EXISTS ux_appointments_active_slot;
                DROP INDEX IF EXISTS ux_appointments_active_slot_staff;
                DROP INDEX IF EXISTS ux_appointments_active_doctor;
                DROP INDEX IF EXISTS ux_appointments_active_specialist;
                DROP INDEX IF EXISTS ux_appt_doctor_time_status;
                DROP INDEX IF EXISTS ux_appt_specialist_time_status;
                DROP INDEX IF EXISTS ux_appt_staff_time_status;
                DROP INDEX IF EXISTS ux_doctor_slot;
                DROP INDEX IF EXISTS ux_specialist_slot;
                DROP INDEX IF EXISTS ux_staff_slot;
                DROP INDEX IF EXISTS ux_national_lock;
                DROP INDEX IF EXISTS ux_mobile_lock;
                DROP INDEX IF EXISTS ux_patients_national;
                """;
            drop.ExecuteNonQuery();
        }
        CancelOlderDuplicateSlots(connection);
        using (var indexes = connection.CreateCommand())
        {
            indexes.CommandText = """
                CREATE UNIQUE INDEX IF NOT EXISTS ux_appointments_active_doctor
                  ON appointments(starts_at_local, doctor_id)
                  WHERE deleted_utc IS NULL AND status='scheduled' AND doctor_id IS NOT NULL;

                CREATE UNIQUE INDEX IF NOT EXISTS ux_appointments_active_specialist
                  ON appointments(starts_at_local, specialist_id)
                  WHERE deleted_utc IS NULL AND status='scheduled' AND specialist_id IS NOT NULL;

                CREATE UNIQUE INDEX IF NOT EXISTS ux_patients_national_active
                  ON patients(national_id) WHERE deleted_utc IS NULL;

                CREATE UNIQUE INDEX IF NOT EXISTS ux_patients_mobile_active
                  ON patients(mobile) WHERE deleted_utc IS NULL;
                """;
            indexes.ExecuteNonQuery();
        }

        SeedDefaultStaff(connection);
        SeedDefaultClinics(connection);
        using var version = connection.CreateCommand();
        version.CommandText = "UPDATE schema_info SET version=5;";
        version.ExecuteNonQuery();
    }

    public static void VerifyHealth()
    {
        using var connection = Open();
        using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA quick_check;";
            if (!string.Equals(integrity.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("فشل فحص سلامة قاعدة البيانات.");
        }

        using var schema = connection.CreateCommand();
        schema.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type='table' AND name IN (
              'patients','appointments','staff','clinics','closure_dates','app_settings','audit_log'
            );
            """;
        if (Convert.ToInt32(schema.ExecuteScalar(), CultureInfo.InvariantCulture) != 7)
            throw new InvalidOperationException("مخطط قاعدة البيانات غير مكتمل.");
    }



    private static void SeedDefaultStaff(SqliteConnection connection)
    {
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM staff;";
        if (Convert.ToInt32(count.ExecuteScalar()) > 0) return;

        var seed = new (string Name, string Role)[]
        {
            ("طبيب عام", "doctor"),
            ("طبيب أسنان", "doctor"),
            ("طبيب باطنة", "doctor"),
            ("طبيب أطفال", "doctor"),
            ("أخصائي علاج طبيعي", "specialist"),
            ("أخصائي تغذية علاجية", "specialist"),
            ("أخصائي نطق وسمع", "specialist"),
        };
        foreach (var (name, role) in seed)
        {
            using var ins = connection.CreateCommand();
            ins.CommandText = "INSERT INTO staff(full_name, role, is_active) VALUES($n,$r,1);";
            ins.Parameters.AddWithValue("$n", name);
            ins.Parameters.AddWithValue("$r", role);
            ins.ExecuteNonQuery();
        }
    }

    private static void MigratePatientFileNumbersToCodes(SqliteConnection connection)
    {
        using var schema = connection.CreateCommand();
        schema.CommandText = "PRAGMA table_info(patients);";
        using var schemaReader = schema.ExecuteReader();
        var fileNumberType = string.Empty;
        while (schemaReader.Read())
        {
            if (string.Equals(schemaReader.GetString(1), "file_number", StringComparison.OrdinalIgnoreCase))
            {
                fileNumberType = schemaReader.IsDBNull(2) ? string.Empty : schemaReader.GetString(2);
                break;
            }
        }
        schemaReader.Close();
        if (fileNumberType.Contains("TEXT", StringComparison.OrdinalIgnoreCase)) return;

        var rows = new List<LegacyPatientRow>();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = """
                SELECT id, file_number, full_name, national_id, mobile, secondary_contact,
                       brief_medical_info, gender, residency_number, blood_type, chronic_diseases,
                       current_medications, drug_allergies, created_utc, updated_utc,
                       last_activity_utc, deleted_utc
                FROM patients ORDER BY id;
                """;
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new LegacyPatientRow(
                    reader.GetInt64(0), Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                    reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.GetString(13), reader.GetString(14), reader.GetString(15),
                    reader.IsDBNull(16) ? null : reader.GetString(16)));
            }
        }

        CreateMigrationBackup(connection);
        using (var foreignKeysOff = connection.CreateCommand())
        {
            foreignKeysOff.CommandText = "PRAGMA foreign_keys=OFF; PRAGMA ignore_check_constraints=ON;";
            foreignKeysOff.ExecuteNonQuery();
        }

        try
        {
            using var transaction = connection.BeginTransaction();
            using (var create = connection.CreateCommand())
            {
                create.Transaction = transaction;
                create.CommandText = """
                    DROP TABLE IF EXISTS patients_codes;
                    CREATE TABLE patients_codes (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        file_number TEXT NOT NULL UNIQUE,
                        full_name TEXT NOT NULL CHECK(length(trim(full_name)) >= 3),
                        national_id TEXT NOT NULL CHECK(national_id NOT GLOB '*[^0-9]*' AND length(national_id) = 10),
                        mobile TEXT NOT NULL CHECK(mobile NOT GLOB '*[^0-9]*' AND length(mobile) BETWEEN 9 AND 10),
                        secondary_contact TEXT,
                        brief_medical_info TEXT,
                        gender TEXT NOT NULL DEFAULT 'male' CHECK(gender IN ('male','female')),
                        residency_number TEXT,
                        blood_type TEXT,
                        chronic_diseases TEXT,
                        current_medications TEXT,
                        drug_allergies TEXT,
                        created_utc TEXT NOT NULL,
                        updated_utc TEXT NOT NULL,
                        last_activity_utc TEXT NOT NULL,
                        deleted_utc TEXT
                    );
                    """;
                create.ExecuteNonQuery();
            }

            foreach (var row in rows)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO patients_codes(
                      id, file_number, full_name, national_id, mobile, secondary_contact,
                      brief_medical_info, gender, residency_number, blood_type, chronic_diseases,
                      current_medications, drug_allergies, created_utc, updated_utc,
                      last_activity_utc, deleted_utc)
                    VALUES($id,$file,$name,$national,$mobile,$secondary,$medical,$gender,$residency,
                      $blood,$chronic,$medications,$allergies,$created,$updated,$activity,$deleted);
                    """;
                insert.Parameters.AddWithValue("$id", row.Id);
                insert.Parameters.AddWithValue("$file", LegacyFileCode(row.LegacyFileNumber));
                insert.Parameters.AddWithValue("$name", row.FullName);
                insert.Parameters.AddWithValue("$national", row.NationalId);
                insert.Parameters.AddWithValue("$mobile", row.Mobile);
                insert.Parameters.AddWithValue("$secondary", DbValue(row.SecondaryContact));
                insert.Parameters.AddWithValue("$medical", DbValue(row.BriefMedicalInfo));
                insert.Parameters.AddWithValue("$gender", row.Gender is "female" ? "female" : "male");
                insert.Parameters.AddWithValue("$residency", DbValue(row.ResidencyNumber));
                insert.Parameters.AddWithValue("$blood", DbValue(row.BloodType));
                insert.Parameters.AddWithValue("$chronic", DbValue(row.ChronicDiseases));
                insert.Parameters.AddWithValue("$medications", DbValue(row.CurrentMedications));
                insert.Parameters.AddWithValue("$allergies", DbValue(row.DrugAllergies));
                insert.Parameters.AddWithValue("$created", row.CreatedUtc);
                insert.Parameters.AddWithValue("$updated", row.UpdatedUtc);
                insert.Parameters.AddWithValue("$activity", row.LastActivityUtc);
                insert.Parameters.AddWithValue("$deleted", DbValue(row.DeletedUtc));
                insert.ExecuteNonQuery();
            }

            using (var replace = connection.CreateCommand())
            {
                replace.Transaction = transaction;
                replace.CommandText = """
                    DROP TABLE patients;
                    ALTER TABLE patients_codes RENAME TO patients;
                    CREATE INDEX IF NOT EXISTS ix_patients_national ON patients(national_id);
                    CREATE INDEX IF NOT EXISTS ix_patients_name ON patients(full_name);
                    CREATE INDEX IF NOT EXISTS ix_patients_mobile ON patients(mobile);
                    UPDATE schema_info SET version=5;
                    """;
                replace.ExecuteNonQuery();
            }

            using var check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "PRAGMA foreign_key_check;";
            using var invalid = check.ExecuteReader();
            var hasInvalidReference = invalid.Read();
            invalid.Close();
            if (hasInvalidReference) throw new InvalidOperationException("تعذر ترحيل قاعدة البيانات بأمان.");
            transaction.Commit();
        }
        finally
        {
            using var foreignKeysOn = connection.CreateCommand();
            foreignKeysOn.CommandText = "PRAGMA ignore_check_constraints=OFF; PRAGMA foreign_keys=ON;";
            foreignKeysOn.ExecuteNonQuery();
        }
    }

    private static void CreateMigrationBackup(SqliteConnection source)
    {
        AppPaths.EnsureCreated();
        var backup = Path.Combine(
            AppPaths.Backups,
            $"قبل-ترحيل-الإصدار-7.0.9-{DateTime.Now:yyyyMMdd-HHmmss}.sqlite3");
        using var destination = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = backup }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static string LegacyFileCode(int value)
    {
        var sequence = Math.Max(1, value);
        var prefixNumber = ((sequence - 1) / 9999) + 1;
        var suffix = ((sequence - 1) % 9999) + 1;
        Span<char> buffer = stackalloc char[3];
        var position = buffer.Length;
        while (prefixNumber > 0)
        {
            prefixNumber--;
            buffer[--position] = (char)('A' + prefixNumber % 26);
            prefixNumber /= 26;
        }
        return $"{new string(buffer[position..])}-{suffix:0000}";
    }

    private static object DbValue(string? value) => value is null ? DBNull.Value : value;

    private sealed record LegacyPatientRow(
        long Id, int LegacyFileNumber, string FullName, string NationalId, string Mobile,
        string? SecondaryContact, string? BriefMedicalInfo, string Gender, string? ResidencyNumber,
        string? BloodType, string? ChronicDiseases, string? CurrentMedications, string? DrugAllergies,
        string CreatedUtc, string UpdatedUtc, string LastActivityUtc, string? DeletedUtc);

    private static void SeedDefaultClinics(SqliteConnection connection)
    {
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM clinics WHERE deleted_utc IS NULL;";
        if (Convert.ToInt32(count.ExecuteScalar()) > 0) return;

        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO clinics(full_name, is_active) VALUES($name, 1);";
        insert.Parameters.AddWithValue("$name", "العيادة الرئيسية");
        insert.ExecuteNonQuery();
    }

    private static void SeedSetting(SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO app_settings(setting_key, setting_value, updated_utc)
            VALUES($key, $value, $now);
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }


    private static void CancelOlderDuplicateSlots(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE appointments SET status='cancelled', updated_utc=$now
            WHERE status='scheduled'
              AND id IN (
                SELECT a.id FROM appointments a
                JOIN appointments b
                  ON a.id > b.id AND a.status='scheduled' AND b.status='scheduled'
                 AND a.starts_at_local = b.starts_at_local
                 AND (
                      (a.doctor_id IS NOT NULL AND a.doctor_id = b.doctor_id) OR
                      (a.specialist_id IS NOT NULL AND a.specialist_id = b.specialist_id) OR
                      (a.staff_id IS NOT NULL AND a.staff_id = b.staff_id)
                 )
              );
            """;
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }
        reader.Close();
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }
}
