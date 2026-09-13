using Microsoft.Data.Sqlite;
using System.Globalization;
using SaudiPatientDesk.Data;
using SaudiPatientDesk.Domain;

namespace SaudiPatientDesk.Services;

public sealed class PatientService
{
    public IReadOnlyList<Patient> Search(string? term = null, int limit = 500)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        var normalized = term?.Trim() ?? string.Empty;
        command.CommandText = """
            SELECT id, file_number, full_name, national_id, mobile, secondary_contact,
                   brief_medical_info, created_utc, updated_utc
            FROM patients
            WHERE deleted_utc IS NULL
              AND ($q = '' OR full_name LIKE $like OR national_id = $q OR mobile = $q OR CAST(file_number AS TEXT) = $q)
            ORDER BY file_number DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$q", normalized);
        command.Parameters.AddWithValue("$like", $"%{normalized}%");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 10000));
        using var reader = command.ExecuteReader();
        var items = new List<Patient>();
        while (reader.Read()) items.Add(ReadPatient(reader));
        return items;
    }

    public Patient? FindByFileNumber(int fileNumber)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, file_number, full_name, national_id, mobile, secondary_contact,
                   brief_medical_info, created_utc, updated_utc
            FROM patients WHERE file_number=$number AND deleted_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$number", fileNumber);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadPatient(reader) : null;
    }

    public Patient Add(PatientDraft draft)
    {
        var error = Validation.Patient(draft.FullName, draft.NationalId, draft.Mobile);
        if (error is not null) throw new InvalidOperationException(error);

        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();
        EnsureNoDuplicate(connection, transaction, draft.NationalId, draft.Mobile, null);

        var nextNumber = NextFileNumber(connection, transaction);
        var now = DateTime.UtcNow.ToString("O");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO patients(file_number, full_name, national_id, mobile, secondary_contact,
              brief_medical_info, created_utc, updated_utc, last_activity_utc)
            VALUES($file, $name, $national, $mobile, $secondary, $medical, $now, $now, $now);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$file", nextNumber);
        BindDraft(command, draft);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteScalar();
        transaction.Commit();
        return FindByFileNumber(nextNumber) ?? throw new InvalidOperationException("تعذر قراءة السجل بعد حفظه.");
    }

    public void Update(long id, PatientDraft draft)
    {
        var error = Validation.Patient(draft.FullName, draft.NationalId, draft.Mobile);
        if (error is not null) throw new InvalidOperationException(error);
        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();
        EnsureNoDuplicate(connection, transaction, draft.NationalId, draft.Mobile, id);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE patients SET full_name=$name, national_id=$national, mobile=$mobile,
              secondary_contact=$secondary, brief_medical_info=$medical,
              updated_utc=$now, last_activity_utc=$now
            WHERE id=$id AND deleted_utc IS NULL;
            """;
        BindDraft(command, draft);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("السجل غير موجود.");
        transaction.Commit();
    }

    public void SoftDelete(long id)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE patients SET deleted_utc=$now, updated_utc=$now WHERE id=$id AND deleted_utc IS NULL;";
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static int NextFileNumber(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(file_number), 0) + 1 FROM patients;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void EnsureNoDuplicate(SqliteConnection connection, SqliteTransaction transaction,
        string nationalId, string mobile, long? exceptId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT file_number FROM patients
            WHERE deleted_utc IS NULL AND (national_id=$national OR mobile=$mobile)
              AND ($except IS NULL OR id <> $except)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$national", nationalId);
        command.Parameters.AddWithValue("$mobile", mobile);
        command.Parameters.AddWithValue("$except", exceptId is null ? DBNull.Value : exceptId.Value);
        var duplicate = command.ExecuteScalar();
        if (duplicate is not null)
            throw new InvalidOperationException($"يوجد مريض مسجل مسبقاً برقم ملف {duplicate}.");
    }

    private static void BindDraft(SqliteCommand command, PatientDraft draft)
    {
        command.Parameters.AddWithValue("$name", draft.FullName.Trim());
        command.Parameters.AddWithValue("$national", draft.NationalId.Trim());
        command.Parameters.AddWithValue("$mobile", draft.Mobile.Trim());
        command.Parameters.AddWithValue("$secondary", (object?)draft.SecondaryContact?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$medical", (object?)draft.BriefMedicalInfo?.Trim() ?? DBNull.Value);
    }

    private static Patient ReadPatient(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
        DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
}
