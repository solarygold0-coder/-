using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.RegularExpressions;
using SaudiPatientDesk.Data;
using SaudiPatientDesk.Domain;

namespace SaudiPatientDesk.Services;

public sealed class PatientService
{
    private static readonly Regex CodeRegex = new(
        @"^([A-Z]{1,3})-([0-9]{4})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const string PatientSelect = """
        SELECT id, file_number, full_name, national_id, mobile, secondary_contact,
               brief_medical_info, gender, residency_number, blood_type,
               chronic_diseases, current_medications, drug_allergies,
               created_utc, updated_utc
        FROM patients
        """;

    public IReadOnlyList<Patient> Search(string? term = null, int limit = 500)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        var normalized = term?.Trim() ?? string.Empty;
        var normalizedFile = normalized.ToUpperInvariant();
        command.CommandText = PatientSelect + """
            WHERE deleted_utc IS NULL
              AND ($q = '' OR full_name LIKE $like OR national_id = $q OR mobile = $q
                   OR file_number = $file OR file_number LIKE $fileLike
                   OR IFNULL(residency_number,'') = $q)
            ORDER BY file_number COLLATE NOCASE
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$q", normalized);
        command.Parameters.AddWithValue("$like", $"%{normalized}%");
        command.Parameters.AddWithValue("$file", normalizedFile);
        command.Parameters.AddWithValue("$fileLike", $"%{normalizedFile}%");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 10000));
        using var reader = command.ExecuteReader();
        var items = new List<Patient>();
        while (reader.Read()) items.Add(ReadPatient(reader));
        return items;
    }

    public Patient? FindByFileNumber(string fileNumber)
    {
        var normalized = Validation.NormalizeFileNumber(fileNumber);
        if (Validation.FileNumber(normalized) is not null) return null;

        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = PatientSelect +
            " WHERE file_number=$number AND deleted_utc IS NULL;";
        command.Parameters.AddWithValue("$number", normalized);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadPatient(reader) : null;
    }

    public Patient Add(PatientDraft draft)
    {
        var error = Validation.Patient(draft.FullName, draft.NationalId, draft.Mobile, draft.Gender);
        if (error is not null) throw new InvalidOperationException(error);

        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();

        var fileNumber = string.IsNullOrWhiteSpace(draft.FileNumber)
            ? NextSequentialFileNumber(connection, transaction)
            : Validation.NormalizeFileNumber(draft.FileNumber);

        var fileError = Validation.FileNumber(fileNumber);
        if (fileError is not null) throw new InvalidOperationException(fileError);

        EnsureNoDuplicate(connection, transaction, draft.NationalId, draft.Mobile, null);
        EnsureFileNumberAvailable(connection, transaction, fileNumber, null);

        var now = DateTime.UtcNow.ToString("O");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO patients(
              file_number, full_name, national_id, mobile, secondary_contact, brief_medical_info,
              gender, residency_number, blood_type, chronic_diseases, current_medications, drug_allergies,
              created_utc, updated_utc, last_activity_utc)
            VALUES(
              $file, $name, $national, $mobile, $secondary, $medical,
              $gender, $residency, $blood, $chronic, $meds, $allergies,
              $now, $now, $now);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$file", fileNumber);
        BindDraft(command, draft);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteScalar();
        transaction.Commit();
        Database.Log("patient.add", fileNumber);
        return FindByFileNumber(fileNumber) ?? throw new InvalidOperationException("تعذر قراءة السجل بعد حفظه.");
    }

    public void Update(long id, PatientDraft draft)
    {
        var fileError = Validation.FileNumber(draft.FileNumber);
        if (fileError is not null) throw new InvalidOperationException(fileError);
        var error = Validation.Patient(draft.FullName, draft.NationalId, draft.Mobile, draft.Gender);
        if (error is not null) throw new InvalidOperationException(error);

        var fileNumber = Validation.NormalizeFileNumber(draft.FileNumber);

        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();
        EnsureNoDuplicate(connection, transaction, draft.NationalId, draft.Mobile, id);
        EnsureFileNumberAvailable(connection, transaction, fileNumber, id);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE patients SET
              file_number=$file, full_name=$name, national_id=$national, mobile=$mobile,
              secondary_contact=$secondary, brief_medical_info=$medical,
              gender=$gender, residency_number=$residency, blood_type=$blood,
              chronic_diseases=$chronic, current_medications=$meds, drug_allergies=$allergies,
              updated_utc=$now, last_activity_utc=$now
            WHERE id=$id AND deleted_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$file", fileNumber);
        BindDraft(command, draft);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("السجل غير موجود.");
        transaction.Commit();
        Database.Log("patient.update", fileNumber);
    }

    public void SoftDelete(long id)
    {
        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");

        using (var cancel = connection.CreateCommand())
        {
            cancel.Transaction = transaction;
            cancel.CommandText = """
                UPDATE appointments
                SET status='cancelled', updated_utc=$now
                WHERE patient_id=$id
                  AND deleted_utc IS NULL
                  AND status='scheduled'
                  AND starts_at_local >= $from;
                """;
            cancel.Parameters.AddWithValue("$now", now);
            cancel.Parameters.AddWithValue("$id", id);
            cancel.Parameters.AddWithValue("$from", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            cancel.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                "UPDATE patients SET deleted_utc=$now, updated_utc=$now WHERE id=$id AND deleted_utc IS NULL;";
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$id", id);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("السجل غير موجود أو محذوف مسبقاً.");
        }

        using (var files = connection.CreateCommand())
        {
            files.Transaction = transaction;
            files.CommandText = "UPDATE attachments SET deleted_utc=$now WHERE patient_id=$id AND deleted_utc IS NULL;";
            files.Parameters.AddWithValue("$now", now);
            files.Parameters.AddWithValue("$id", id);
            files.ExecuteNonQuery();
        }


        transaction.Commit();
        Database.Log("patient.delete", id.ToString());
    }

    public string GenerateFileNumber()
    {
        using var connection = Database.Open();
        return NextSequentialFileNumber(connection, null);
    }

    private static string NextSequentialFileNumber(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        if (transaction is not null) command.Transaction = transaction;
        command.CommandText =
            "SELECT file_number FROM patients WHERE file_number GLOB '[A-Z]*-[0-9][0-9][0-9][0-9]';";

        string bestPrefix = "A";
        int bestNumber = 0;
        var found = false;

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var raw = reader.GetString(0);
                var m = CodeRegex.Match(raw);
                if (!m.Success) continue;
                var prefix = m.Groups[1].Value;
                var number = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (!found || CompareCode(prefix, number, bestPrefix, bestNumber) > 0)
                {
                    bestPrefix = prefix;
                    bestNumber = number;
                    found = true;
                }
            }
        }

        if (!found)
            return "A-0001";

        if (bestNumber < 9999)
            return $"{bestPrefix}-{(bestNumber + 1).ToString("D4", CultureInfo.InvariantCulture)}";

        return $"{IncrementPrefix(bestPrefix)}-0001";
    }

    private static string IncrementPrefix(string prefix)
    {
        var chars = prefix.ToCharArray();
        for (var i = chars.Length - 1; i >= 0; i--)
        {
            if (chars[i] < 'Z')
            {
                chars[i]++;
                return new string(chars);
            }
            chars[i] = 'A';
        }
        return "A" + new string(chars);
    }

    private static int CompareCode(string p1, int n1, string p2, int n2)
    {
        var len = p1.Length.CompareTo(p2.Length);
        if (len != 0) return len;
        var prefix = string.CompareOrdinal(p1, p2);
        if (prefix != 0) return prefix;
        return n1.CompareTo(n2);
    }

    private static void EnsureFileNumberAvailable(
        SqliteConnection connection, SqliteTransaction transaction, string fileNumber, long? exceptId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM patients
            WHERE file_number=$file
              AND ($except IS NULL OR id <> $except)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$file", fileNumber);
        command.Parameters.AddWithValue("$except", exceptId is null ? DBNull.Value : exceptId.Value);
        if (command.ExecuteScalar() is not null)
            throw new InvalidOperationException($"رقم الملف «{fileNumber}» مستخدم مسبقاً (حتى لو كان محذوفاً).");
    }

    private static void EnsureNoDuplicate(
        SqliteConnection connection, SqliteTransaction transaction,
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
        command.Parameters.AddWithValue("$gender", draft.Gender);
        command.Parameters.AddWithValue("$residency", (object?)draft.ResidencyNumber?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$blood", (object?)draft.BloodType?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$chronic", (object?)draft.ChronicDiseases?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$meds", (object?)draft.CurrentMedications?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$allergies", (object?)draft.DrugAllergies?.Trim() ?? DBNull.Value);
    }

    private static Patient ReadPatient(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? "male" : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.IsDBNull(12) ? null : reader.GetString(12),
        DateTime.Parse(reader.GetString(13), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        DateTime.Parse(reader.GetString(14), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
}
