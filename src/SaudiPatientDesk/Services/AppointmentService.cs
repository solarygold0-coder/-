using System.Globalization;
using Microsoft.Data.Sqlite;
using SaudiPatientDesk.Data;
using SaudiPatientDesk.Domain;

namespace SaudiPatientDesk.Services;

public sealed class AppointmentService
{
    private readonly PatientService _patients = new();
    private readonly SettingsService _settings = new();

    public Appointment Add(string fileNumber, DateTime startsAt, string? notes, long? staffId = null, long? doctorId = null, long? specialistId = null, long? clinicId = null, string kind = "regular")
    {
        var patient = _patients.FindByFileNumber(fileNumber)
            ?? throw new InvalidOperationException("رقم الملف غير موجود.");
        var error = Validation.Appointment(startsAt, _settings);
        if (error is not null) throw new InvalidOperationException(error);
        var hoursError = ClinicHours.Load(_settings).ValidateAppointmentTime(startsAt);
        if (hoursError is not null) throw new InvalidOperationException(hoursError);
        ValidateClinicians(doctorId, specialistId);

        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        EnsureCliniciansAreActive(connection, transaction, doctorId, specialistId);
        EnsureClinicIsOpen(connection, transaction, startsAt);
        if (clinicId is long cid)
            ClinicService.EnsureClinicAllowedOnDay(connection, transaction, cid, startsAt);
        EnsureClinicianSlotsFree(connection, transaction, startsAt, doctorId, specialistId, null);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO appointments(patient_id, starts_at_local, notes, created_utc, updated_utc, staff_id,
              doctor_id, specialist_id, clinic_id, kind, visit_stage)
            VALUES($patient, $start, $notes, $now, $now, $staff,
              $doctor, $specialist, $clinic, $kind, $stage);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$patient", patient.Id);
        command.Parameters.AddWithValue("$start", FormatLocal(startsAt));
        command.Parameters.AddWithValue("$notes", (object?)notes?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        var effectiveStaffId = staffId ?? doctorId ?? specialistId;
        command.Parameters.AddWithValue("$staff", effectiveStaffId is null ? DBNull.Value : effectiveStaffId.Value);
        command.Parameters.AddWithValue("$doctor", doctorId is null ? DBNull.Value : doctorId.Value);
        command.Parameters.AddWithValue("$specialist", specialistId is null ? DBNull.Value : specialistId.Value);
        command.Parameters.AddWithValue("$clinic", clinicId is null ? DBNull.Value : clinicId.Value);
        command.Parameters.AddWithValue("$kind", string.IsNullOrWhiteSpace(kind) ? "regular" : kind);
        command.Parameters.AddWithValue("$stage", string.Empty);
        long id;
        try
        {
            id = (long)(command.ExecuteScalar() ?? 0L);
        }
        catch (SqliteException ex) when (IsScheduleConflict(ex))
        {
            throw MapScheduleConflict(ex);
        }
        TouchLastActivity(connection, patient.Id, transaction);
        transaction.Commit();
        Database.Log("appointment.add", fileNumber + " " + FormatLocal(startsAt));
        return Find(id) ?? new Appointment(id, patient.Id, patient.FileNumber, patient.FullName, startsAt, "scheduled", notes, effectiveStaffId);
    }

    public Appointment Update(long appointmentId, string fileNumber, DateTime startsAt, string? notes, long? staffId = null, long? doctorId = null, long? specialistId = null, long? clinicId = null, string kind = "regular")
    {
        var patient = _patients.FindByFileNumber(fileNumber)
            ?? throw new InvalidOperationException("رقم الملف غير موجود.");
        var error = Validation.Appointment(startsAt, _settings);
        if (error is not null) throw new InvalidOperationException(error);
        var hoursError = ClinicHours.Load(_settings).ValidateAppointmentTime(startsAt);
        if (hoursError is not null) throw new InvalidOperationException(hoursError);
        ValidateClinicians(doctorId, specialistId);

        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        EnsureCliniciansAreActive(connection, transaction, doctorId, specialistId);
        EnsureClinicIsOpen(connection, transaction, startsAt);
        if (clinicId is long cid)
            ClinicService.EnsureClinicAllowedOnDay(connection, transaction, cid, startsAt, appointmentId);
        EnsureClinicianSlotsFree(connection, transaction, startsAt, doctorId, specialistId, appointmentId);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE appointments
            SET patient_id=$patient, starts_at_local=$start, notes=$notes, updated_utc=$now, staff_id=$staff,
                doctor_id=$doctor, specialist_id=$specialist, clinic_id=$clinic, kind=$kind,
                status=CASE WHEN starts_at_local <> $start THEN 'scheduled' ELSE status END
            WHERE id=$id AND deleted_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$patient", patient.Id);
        command.Parameters.AddWithValue("$start", FormatLocal(startsAt));
        command.Parameters.AddWithValue("$notes", (object?)notes?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", appointmentId);
        var effectiveStaffId = staffId ?? doctorId ?? specialistId;
        command.Parameters.AddWithValue("$staff", effectiveStaffId is null ? DBNull.Value : effectiveStaffId.Value);
        command.Parameters.AddWithValue("$doctor", doctorId is null ? DBNull.Value : doctorId.Value);
        command.Parameters.AddWithValue("$specialist", specialistId is null ? DBNull.Value : specialistId.Value);
        command.Parameters.AddWithValue("$clinic", clinicId is null ? DBNull.Value : clinicId.Value);
        command.Parameters.AddWithValue("$kind", string.IsNullOrWhiteSpace(kind) ? "regular" : kind);
        try
        {
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("الموعد غير موجود أو تم حذفه.");
        }
        catch (SqliteException ex) when (IsScheduleConflict(ex))
        {
            throw MapScheduleConflict(ex);
        }
        TouchLastActivity(connection, patient.Id, transaction);
        ClinicService.ReleaseUnusedClinicDays(connection, transaction);
        transaction.Commit();
        Database.Log("appointment.update", fileNumber + " " + FormatLocal(startsAt));

        return Find(appointmentId) ?? throw new InvalidOperationException("تعذر قراءة الموعد بعد حفظ التعديل.");
    }

    public void Cancel(long appointmentId)
    {
        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE appointments SET status='cancelled', updated_utc=$now
            WHERE id=$id AND deleted_utc IS NULL AND status <> 'cancelled';
            """;
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", appointmentId);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("الموعد ملغي مسبقاً أو غير موجود.");
        TouchLastActivityForAppointment(connection, appointmentId, transaction);
        ClinicService.ReleaseUnusedClinicDays(connection, transaction);
        transaction.Commit();
        Database.Log("appointment.cancel", appointmentId.ToString());
    }

    public bool IsSlotFree(DateTime startsAt, long? exceptAppointmentId = null, long? staffId = null)
    {
        if (staffId is null) return true;
        using var connection = Database.Open();
        try
        {
            EnsureClinicianSlotsFree(connection, null, startsAt, staffId, staffId, exceptAppointmentId);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public string? DescribeSlot(DateTime startsAt, long? exceptAppointmentId = null,
        long? doctorId = null, long? specialistId = null)
    {
        if (_settings.IsWeeklyClosed(startsAt))
            return "مغلق أسبوعياً حسب الإعدادات";
        using var connection = Database.Open();
        using var closure = connection.CreateCommand();
        closure.CommandText = "SELECT reason FROM closure_dates WHERE closed_date=$date;";
        closure.Parameters.AddWithValue("$date", startsAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (closure.ExecuteScalar() is string reason)
            return "مغلق: " + reason;
        var hours = ClinicHours.Load(_settings);
        if (hours.ValidateAppointmentTime(startsAt) is { } timeError)
            return timeError;
        if (doctorId is null && specialistId is null)
            return "اختر الطبيب أو الأخصائي لمعرفة توفر الخانة.";
        try
        {
            EnsureClinicianSlotsFree(connection, null, startsAt, doctorId, specialistId, exceptAppointmentId);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
        return "متاح";
    }

    private static void EnsureClinicIsOpen(
        SqliteConnection connection, SqliteTransaction transaction, DateTime startsAt)
    {
        using var closure = connection.CreateCommand();
        closure.Transaction = transaction;
        closure.CommandText = "SELECT reason FROM closure_dates WHERE closed_date=$date;";
        closure.Parameters.AddWithValue("$date", startsAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (closure.ExecuteScalar() is string reason)
            throw new InvalidOperationException("العيادة مغلقة في هذا اليوم: " + reason);
    }

    private static void TouchLastActivity(
        SqliteConnection connection, long patientId, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE patients SET last_activity_utc=$now, updated_utc=$now WHERE id=$id AND deleted_utc IS NULL;";
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", patientId);
        command.ExecuteNonQuery();
    }

    private static void TouchLastActivityForAppointment(
        SqliteConnection connection, long appointmentId, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE patients SET last_activity_utc=$now, updated_utc=$now
            WHERE deleted_utc IS NULL AND id=(
              SELECT patient_id FROM appointments WHERE id=$appointment AND deleted_utc IS NULL
            );
            """;
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$appointment", appointmentId);
        command.ExecuteNonQuery();
    }

    private static bool IsScheduleConflict(SqliteException exception) =>
        exception.SqliteErrorCode == 19 &&
        exception.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException MapScheduleConflict(SqliteException exception)
    {
        if (exception.Message.Contains("doctor_id", StringComparison.OrdinalIgnoreCase))
            return new InvalidOperationException("هذا الوقت محجوز للطبيب المحدد.", exception);
        if (exception.Message.Contains("specialist_id", StringComparison.OrdinalIgnoreCase))
            return new InvalidOperationException("هذا الوقت محجوز للأخصائي المحدد.", exception);
        return new InvalidOperationException("تعارض الموعد مع حجز قائم.", exception);
    }

    private static string FormatLocal(DateTime value)
    {
        var text = value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        if (text.Length != 16)
            throw new InvalidOperationException("تنسيق الوقت غير صالح.");
        return text;
    }

    private static DateTime ParseLocal(string value) =>
        DateTime.ParseExact(value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None);


    public Appointment? Find(long id)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id, p.id, p.file_number, p.full_name, a.starts_at_local, a.status, a.notes,
                   a.staff_id, s.full_name, a.doctor_id, d.full_name, a.specialist_id, sp.full_name,
                   a.clinic_id, c.full_name, IFNULL(a.kind,'regular')
            FROM appointments a JOIN patients p ON p.id=a.patient_id
            LEFT JOIN staff s ON s.id=a.staff_id
            LEFT JOIN staff d ON d.id=a.doctor_id
            LEFT JOIN staff sp ON sp.id=a.specialist_id
            LEFT JOIN clinics c ON c.id=a.clinic_id
            WHERE a.id=$id AND a.deleted_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAppointment(reader) : null;
    }

    public void SetVisitStage(long appointmentId, string stage)
    {
        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE appointments SET visit_stage=$s, updated_utc=$now WHERE id=$id AND deleted_utc IS NULL;";
        command.Parameters.AddWithValue("$s", stage ?? "");
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", appointmentId);
        command.ExecuteNonQuery();
        TouchLastActivityForAppointment(connection, appointmentId, transaction);
        transaction.Commit();
    }

    public void SetStatus(long appointmentId, string status)
    {
        if (status is not ("scheduled" or "completed" or "cancelled" or "missed"))
            throw new InvalidOperationException("حالة الموعد غير صحيحة.");
        using var connection = Database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE appointments SET status=$s, updated_utc=$now
            WHERE id=$id AND deleted_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$s", status);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", appointmentId);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("تعذر تحديث حالة الموعد.");
        TouchLastActivityForAppointment(connection, appointmentId, transaction);
        if (status != "scheduled")
            ClinicService.ReleaseUnusedClinicDays(connection, transaction);
        transaction.Commit();
    }

    public IReadOnlyList<TodayRow> TodayList(DateTime? day = null, long? staffId = null)
    {
        var date = (day ?? DateTime.Today).Date;
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id, p.id, p.file_number, p.full_name, a.starts_at_local, a.status, a.notes,
                   a.staff_id, s.full_name, a.doctor_id, d.full_name, a.specialist_id, sp.full_name,
                   a.clinic_id, c.full_name, IFNULL(a.kind,'regular'),
                   p.drug_allergies, p.chronic_diseases, p.mobile,
                   IFNULL(a.visit_stage,'')
            FROM appointments a
            JOIN patients p ON p.id=a.patient_id
            LEFT JOIN staff s ON s.id=a.staff_id
            LEFT JOIN staff d ON d.id=a.doctor_id
            LEFT JOIN staff sp ON sp.id=a.specialist_id
            LEFT JOIN clinics c ON c.id=a.clinic_id
            WHERE a.deleted_utc IS NULL AND p.deleted_utc IS NULL
              AND a.starts_at_local >= $start AND a.starts_at_local < $end
              AND a.status <> 'cancelled'
              AND ($staff IS NULL OR a.staff_id=$staff OR a.doctor_id=$staff OR a.specialist_id=$staff)
            ORDER BY a.starts_at_local;
            """;
        command.Parameters.AddWithValue("$start", FormatLocal(date));
        command.Parameters.AddWithValue("$end", FormatLocal(date.AddDays(1)));
        command.Parameters.AddWithValue("$staff", staffId is null ? DBNull.Value : staffId.Value);
        using var reader = command.ExecuteReader();
        var rows = new List<TodayRow>();
        while (reader.Read())
        {
            var appt = ReadAppointment(reader);
            var allergy = !reader.IsDBNull(16) && reader.GetString(16).Trim().Length > 0;
            var chronic = !reader.IsDBNull(17) && reader.GetString(17).Trim().Length > 0;
            var mobile = !reader.IsDBNull(18) ? reader.GetString(18) : "";
            var stage = !reader.IsDBNull(19) ? reader.GetString(19) : "";
            rows.Add(new TodayRow(appt, allergy, chronic, appt.ClinicianSummary, mobile, stage));
        }
        return rows;
    }

    public IReadOnlyList<TimelineSlot> BuildTimeline(DateTime day, long? staffId = null)
    {
        var hours = ClinicHours.Load(_settings);
        var slots = new List<TimelineSlot>();
        var cursor = day.Date.Add(hours.Start);
        var end = day.Date.Add(hours.End);
        var booked = TodayList(day, staffId)
            .Where(r => r.Appointment.Status is "scheduled" or "completed" or "missed")
            .GroupBy(r => r.Appointment.StartsAt)
            .ToDictionary(group => group.Key, group => group.ToList());

        while (cursor < end)
        {
            if (hours.IsOnBreak(cursor))
            {
                slots.Add(new TimelineSlot(cursor, "break", TimeDisplay.Format12(cursor), "استراحة", null, null));
            }
            else if (booked.TryGetValue(cursor, out var rows))
            {
                var first = rows[0].Appointment;
                var caption = rows.Count > 1
                    ? $"{rows.Count} مواعيد"
                    : first.PatientName[..Math.Min(2, first.PatientName.Length)];
                var patientNames = string.Join("، ", rows.Select(row => row.Appointment.PatientName));
                slots.Add(new TimelineSlot(cursor, "booked", TimeDisplay.Format12(cursor), caption,
                    rows.Count == 1 ? first.Id : null, patientNames));
            }
            else
            {
                var kind = "available";
                var cap = Loc.T("dash.available");
                if (cursor <= DateTime.Now && cursor.AddMinutes(hours.SlotMinutes) > DateTime.Now)
                {
                    kind = "now";
                    cap = Loc.T("dash.now");
                }
                slots.Add(new TimelineSlot(cursor, kind, TimeDisplay.Format12(cursor), cap, null, null));
            }
            cursor = cursor.AddMinutes(hours.SlotMinutes);
        }
        return slots;
    }


    private static void ValidateClinicians(long? doctorId, long? specialistId)
    {
        if (doctorId is null && specialistId is null)
            throw new InvalidOperationException("اختر طبيباً أو أخصائياً أو كليهما.");
        if (doctorId is not null && specialistId is not null && doctorId == specialistId)
            throw new InvalidOperationException("لا يمكن تعيين نفس الشخص طبيباً وأخصائياً معاً.");
    }

    private static void EnsureClinicianSlotsFree(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateTime startsAt,
        long? doctorId,
        long? specialistId,
        long? exceptAppointmentId)
    {
        void Check(string column, long id, string label)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                SELECT 1 FROM appointments
                WHERE deleted_utc IS NULL AND status='scheduled'
                  AND starts_at_local=$start AND {column}=$sid
                  AND ($except IS NULL OR id <> $except)
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$start", startsAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$sid", id);
            command.Parameters.AddWithValue("$except", exceptAppointmentId is null ? DBNull.Value : exceptAppointmentId.Value);
            if (command.ExecuteScalar() is not null)
                throw new InvalidOperationException($"هذا الوقت محجوز لنفس ال{label}.");
        }
        if (doctorId is long d) Check("doctor_id", d, "طبيب");
        if (specialistId is long s) Check("specialist_id", s, "أخصائي");
    }

    private static void EnsureCliniciansAreActive(
        SqliteConnection connection, SqliteTransaction transaction, long? doctorId, long? specialistId)
    {
        void Check(long id, string role, string label)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT 1 FROM staff WHERE id=$id AND role=$role AND is_active=1 LIMIT 1;";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$role", role);
            if (command.ExecuteScalar() is null)
                throw new InvalidOperationException($"ال{label} المحدد غير موجود أو غير نشط.");
        }
        if (doctorId is long doctor) Check(doctor, "doctor", "طبيب");
        if (specialistId is long specialist) Check(specialist, "specialist", "أخصائي");
    }

    public IReadOnlyList<Appointment> ListByFilter(AppointmentFilter filter)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        var now = DateTime.Now;
        var extra = filter switch
        {
            AppointmentFilter.Today => " AND a.starts_at_local >= $a AND a.starts_at_local < $b AND a.status <> 'cancelled' ",
            AppointmentFilter.NextWeek => " AND a.starts_at_local >= $a AND a.starts_at_local < $b AND a.status='scheduled' ",
            AppointmentFilter.Missed => " AND a.status='missed' ",
            AppointmentFilter.Renewed => " AND IFNULL(a.kind,'regular')='renewed' AND a.status <> 'cancelled' ",
            AppointmentFilter.AfterThreeMonths => " AND a.starts_at_local >= $a AND a.status='scheduled' ",
            _ => " AND a.starts_at_local >= $a AND a.status='scheduled' "
        };
        command.CommandText = """
            SELECT a.id, p.id, p.file_number, p.full_name, a.starts_at_local, a.status, a.notes,
                   a.staff_id, s.full_name, a.doctor_id, d.full_name, a.specialist_id, sp.full_name,
                   a.clinic_id, c.full_name, IFNULL(a.kind,'regular')
            FROM appointments a
            JOIN patients p ON p.id=a.patient_id
            LEFT JOIN staff s ON s.id=a.staff_id
            LEFT JOIN staff d ON d.id=a.doctor_id
            LEFT JOIN staff sp ON sp.id=a.specialist_id
            LEFT JOIN clinics c ON c.id=a.clinic_id
            WHERE a.deleted_utc IS NULL AND p.deleted_utc IS NULL
            """ + extra + " ORDER BY a.starts_at_local LIMIT 2000;";

        if (filter == AppointmentFilter.Today)
        {
            command.Parameters.AddWithValue("$a", FormatLocal(now.Date));
            command.Parameters.AddWithValue("$b", FormatLocal(now.Date.AddDays(1)));
        }
        else if (filter == AppointmentFilter.NextWeek)
        {
            command.Parameters.AddWithValue("$a", now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$b", now.AddDays(7).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        }
        else if (filter == AppointmentFilter.AfterThreeMonths)
            command.Parameters.AddWithValue("$a", now.AddMonths(3).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        else if (filter == AppointmentFilter.AllUpcoming)
            command.Parameters.AddWithValue("$a", now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));

        using var reader = command.ExecuteReader();
        var items = new List<Appointment>();
        while (reader.Read()) items.Add(ReadAppointment(reader));
        return items;
    }

    private static Appointment ReadAppointment(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetInt64(1),
        reader.GetString(2),
        reader.GetString(3),
        ParseLocal(reader.GetString(4)),
        reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetInt64(7) : null,
        reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetString(8) : null,
        reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetInt64(9) : null,
        reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetString(10) : null,
        reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetInt64(11) : null,
        reader.FieldCount > 12 && !reader.IsDBNull(12) ? reader.GetString(12) : null,
        reader.FieldCount > 13 && !reader.IsDBNull(13) ? reader.GetInt64(13) : null,
        reader.FieldCount > 14 && !reader.IsDBNull(14) ? reader.GetString(14) : null,
        reader.FieldCount > 15 && !reader.IsDBNull(15) ? reader.GetString(15) : "regular");


    public IReadOnlyList<TeamDayRow> BuildTeamDay(DateTime day)
    {
        var hours = ClinicHours.Load(_settings);
        var staff = new StaffService().ListActive();
        var rows = new List<TeamDayRow>();
        foreach (var s in staff)
        {
            var list = TodayList(day, s.Id);
            var booked = list.Count(r => r.Appointment.Status is "scheduled" or "completed" or "missed");
            var next = FindNextFreeForStaff(day, s.Id, hours);
            rows.Add(new TeamDayRow(s.Id, s.FullName, s.RoleDisplay, booked, next));
        }
        return rows;
    }

    private string FindNextFreeForStaff(DateTime day, long staffId, ClinicHours hours)
    {
        foreach (var slot in hours.SlotStarts())
        {
            var candidate = day.Date.Add(slot);
            if (candidate > DateTime.Now && IsSlotFree(candidate, null, staffId))
                return TimeDisplay.Format12(candidate);
        }
        return "—";
    }

    public DashboardSnapshot Snapshot()
    {
        using var connection = Database.Open();
        static int Scalar(SqliteConnection connection, string sql, Action<SqliteCommand>? bind = null)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            bind?.Invoke(command);
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        var today = DateTime.Today;
        var alertEnd = FormatLocal(DateTime.Now.AddDays(2));
        var inactive = DateTime.UtcNow.AddYears(-10).ToString("O", CultureInfo.InvariantCulture);
        return new DashboardSnapshot(
            Scalar(connection, "SELECT COUNT(*) FROM patients WHERE deleted_utc IS NULL;"),
            Scalar(connection, "SELECT COUNT(*) FROM appointments WHERE deleted_utc IS NULL AND status='scheduled' AND starts_at_local >= $today AND starts_at_local < $tomorrow;",
                command =>
                {
                    command.Parameters.AddWithValue("$today", FormatLocal(today));
                    command.Parameters.AddWithValue("$tomorrow", FormatLocal(today.AddDays(1)));
                }),
            Scalar(connection, "SELECT COUNT(*) FROM appointments WHERE deleted_utc IS NULL AND status='scheduled' AND starts_at_local BETWEEN $now AND $end;",
                command =>
                {
                    command.Parameters.AddWithValue("$now", FormatLocal(DateTime.Now));
                    command.Parameters.AddWithValue("$end", alertEnd);
                }),
            Scalar(connection, "SELECT COUNT(*) FROM patients WHERE deleted_utc IS NULL AND last_activity_utc < $inactive;",
                command => command.Parameters.AddWithValue("$inactive", inactive)));
    }
}
