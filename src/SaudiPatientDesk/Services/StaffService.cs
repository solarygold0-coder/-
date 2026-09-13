using Microsoft.Data.Sqlite;
using SaudiPatientDesk.Data;
using SaudiPatientDesk.Domain;

namespace SaudiPatientDesk.Services;

public sealed class StaffService
{
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
        command.ExecuteNonQuery();
    }
}
