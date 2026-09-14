using System.Globalization;
using Microsoft.Data.Sqlite;
using SaudiPatientDesk.Data;
using SaudiPatientDesk.Domain;

namespace SaudiPatientDesk.Services;

public sealed class StaffService
{
    public StaffMember Add(string fullName, string role)
    {
        var name = (fullName ?? string.Empty).Trim();
        if (name.Length < 3)
            throw new InvalidOperationException("أدخل اسم الطبيب أو الأخصائي (3 أحرف على الأقل).");
        if (role is not ("doctor" or "specialist"))
            throw new InvalidOperationException("اختر الصفة: طبيب أو أخصائي.");

        using var connection = Database.Open();
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM staff WHERE trim(full_name)=trim($name) AND role=$role LIMIT 1;";
            exists.Parameters.AddWithValue("$name", name);
            exists.Parameters.AddWithValue("$role", role);
            if (exists.ExecuteScalar() is not null)
                throw new InvalidOperationException("يوجد معالج بالاسم والصفة نفسيهما مسبقاً.");
        }
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO staff(full_name,role,is_active) VALUES($name,$role,1); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$role", role);
        var id = (long)(command.ExecuteScalar() ?? 0L);
        Database.Log("staff.add", $"{name} ({role})");
        return new StaffMember(id, name, role, true);
    }

    public IReadOnlyList<StaffMember> ListActive()
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, full_name, role, is_active
            FROM staff
            WHERE is_active=1
            ORDER BY id;
            """;
        using var reader = command.ExecuteReader();
        var list = new List<StaffMember>();
        while (reader.Read())
            list.Add(new StaffMember(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3) == 1));
        return list;
    }

    public IReadOnlyList<StaffMember> ListAll()
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, full_name, role, is_active FROM staff ORDER BY id;";
        using var reader = command.ExecuteReader();
        var list = new List<StaffMember>();
        while (reader.Read())
            list.Add(new StaffMember(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3) == 1));
        return list;
    }

    public IReadOnlyList<StaffMember> ListDoctors() =>
        ListActive().Where(s => s.Role == "doctor").ToList();

    public IReadOnlyList<StaffMember> ListSpecialists() =>
        ListActive().Where(s => s.Role == "specialist").ToList();

    public void Rename(long id, string fullName)
    {
        var name = (fullName ?? "").Trim();
        if (name.Length < 3)
            throw new InvalidOperationException("أدخل اسم الطبيب أو الأخصائي (3 أحرف على الأقل).");
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE staff SET full_name=$n WHERE id=$id;";
        command.Parameters.AddWithValue("$n", name);
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("المعالج غير موجود.");
    }

    public void SetActive(long id, bool active)
    {
        using var connection = Database.Open();
        if (!active)
        {
            using var future = connection.CreateCommand();
            future.CommandText = """
                SELECT COUNT(*) FROM appointments
                WHERE deleted_utc IS NULL AND status='scheduled' AND starts_at_local >= $now
                  AND (doctor_id=$id OR specialist_id=$id);
                """;
            future.Parameters.AddWithValue("$now", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            future.Parameters.AddWithValue("$id", id);
            if (Convert.ToInt32(future.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                throw new InvalidOperationException("لا يمكن تعطيل المعالج وفي جدوله مواعيد قادمة. انقلها أو ألغها أولاً.");
        }

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE staff SET is_active=$active WHERE id=$id;";
        command.Parameters.AddWithValue("$active", active ? 1 : 0);
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("المعالج غير موجود.");
        Database.Log(active ? "staff.activate" : "staff.deactivate", id.ToString(CultureInfo.InvariantCulture));
    }
}
