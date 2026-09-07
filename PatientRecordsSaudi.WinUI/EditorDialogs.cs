using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.WinUI
{
    internal static class EditorDialogs
    {
        public static async Task<Patient> EditPatientAsync(MainWindow owner, AppDatabase database, Patient original, bool readOnly)
        {
            AppSettings settings = database.GetSettings(); var form = new FormHost(); var file = form.Text("رقم الملف", original == null ? "يُنشأ تلقائيًا عند الحفظ" : original.FileNumber.ToString(), true); var identityType = form.Combo("نوع الهوية", new[] { "هوية وطنية", "إقامة" }, original?.IdentityType); var nationalId = form.Text("رقم الهوية/الإقامة — أرقام فقط", original?.NationalId, false, 10); var name = form.Text("الاسم الكامل", original?.FullName, false, 150); var gender = form.Combo("الجنس", settings.GenderOptions, original?.Gender); var birth = form.Date("تاريخ الميلاد الميلادي", original?.DateOfBirth ?? new DateTime(1990, 1, 1), false); var nationality = form.Text("الجنسية", original?.Nationality ?? "سعودي", false, 80); var mobile = form.Text("الجوال السعودي", original?.Mobile, false, 16); var alternate = form.Text("هاتف بديل", original?.AlternatePhone, false, 16); var city = form.Text("المدينة", original?.City, false, 80); var address = form.Text("العنوان", original?.Address, false, 250); var emergency = form.Text("اسم جهة اتصال الطوارئ", original?.EmergencyContact, false, 120); var emergencyPhone = form.Text("جوال الطوارئ", original?.EmergencyPhone, false, 16); var blood = form.Combo("فصيلة الدم", settings.BloodTypes, original?.BloodType); var allergies = form.Text("الحساسية", original?.Allergies, false, 1000, true); var chronic = form.Text("الأمراض المزمنة", original?.ChronicConditions, false, 1000, true); var notes = form.Text("ملاحظات إدارية", original?.Notes, false, 2000, true);
            if (readOnly) form.Panel.IsHitTestVisible = false; Patient saved = null; ContentDialog dialog = owner.Dialog(original == null ? "إضافة مراجع جديد" : (readOnly ? "عرض ملف المراجع" : "تعديل ملف المراجع"), form.Scroll(), readOnly ? "" : "حفظ", "إغلاق");
            dialog.PrimaryButtonClick += delegate(ContentDialog sender, ContentDialogButtonClickEventArgs args)
            {
                try
                {
                    string error; if (!SaudiValidation.ValidateSaudiIdentity(nationalId.Text, Convert.ToString(identityType.SelectedItem), out error)) throw new InvalidOperationException(error); if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Trim().Length < 4 || !name.Text.Any(char.IsLetter)) throw new InvalidOperationException("أدخل الاسم الكامل بصورة صحيحة."); DateTime birthValue = birth.Value; if (birthValue.Date > DateTime.Today) throw new InvalidOperationException("تاريخ الميلاد لا يمكن أن يكون في المستقبل."); if (!SaudiValidation.ValidateSaudiMobile(mobile.Text, true, out error)) throw new InvalidOperationException(error); if (!SaudiValidation.ValidateSaudiMobile(alternate.Text, false, out error)) throw new InvalidOperationException("الهاتف البديل: " + error); if (!SaudiValidation.ValidateSaudiMobile(emergencyPhone.Text, false, out error)) throw new InvalidOperationException("جوال الطوارئ: " + error); if (string.IsNullOrWhiteSpace(city.Text)) throw new InvalidOperationException("المدينة مطلوبة.");
                    Patient p = Copy(original); p.IdentityType = Convert.ToString(identityType.SelectedItem); p.NationalId = SaudiValidation.NormalizeDigits(nationalId.Text); p.FullName = name.Text.Trim(); p.Gender = Convert.ToString(gender.SelectedItem); p.DateOfBirth = birthValue.Date; p.Nationality = nationality.Text.Trim(); p.Mobile = SaudiValidation.NormalizeSaudiMobile(mobile.Text); p.AlternatePhone = SaudiValidation.NormalizeSaudiMobile(alternate.Text); p.City = city.Text.Trim(); p.Address = address.Text.Trim(); p.EmergencyContact = emergency.Text.Trim(); p.EmergencyPhone = SaudiValidation.NormalizeSaudiMobile(emergencyPhone.Text); p.BloodType = Convert.ToString(blood.SelectedItem); p.Allergies = allergies.Text.Trim(); p.ChronicConditions = chronic.Text.Trim(); p.Notes = notes.Text.Trim(); saved = original == null ? database.AddPatient(p) : SavePatient(database, p); form.ClearError();
                }
                catch (Exception ex) { args.Cancel = true; form.ShowError(ex.Message); }
            };
            await dialog.ShowAsync(); return saved;
        }

        public static async Task<Appointment> EditAppointmentAsync(MainWindow owner, AppDatabase database, Appointment original, long? initialFileNumber)
        {
            AppSettings settings = database.GetSettings(); var form = new FormHost(); var file = form.Text("رقم ملف المراجع — أرقام فقط", original != null ? original.FileNumber.ToString() : initialFileNumber?.ToString(), false, 12); var patientName = form.Text("اسم المراجع", original?.PatientName, true); var nationalId = form.Text("الهوية/الإقامة", "", true); var mobile = form.Text("الجوال", "", true); var title = form.Text("عنوان الموعد", original?.Title, false, 150); var type = form.Combo("نوع الزيارة", settings.VisitTypes, original?.VisitType); var when = form.Date("التاريخ والوقت الميلادي — قوائم دوّارة", original?.StartsAt ?? database.GetNextAvailableAppointmentTime(settings.DefaultAppointmentMinutes), true); var duration = form.Combo("المدة بالدقائق", new[] { "15", "30", "45", "60", "90", "120" }, (original?.DurationMinutes ?? settings.DefaultAppointmentMinutes).ToString()); var status = form.Combo("الحالة", settings.AppointmentStatuses, original?.Status); var notes = form.Text("ملاحظات", original?.Notes, false, 1000, true); Patient patient = null;
            Action resolve = delegate { long n; patient = long.TryParse(SaudiValidation.NormalizeDigits(file.Text), out n) ? database.FindByFileNumber(n, false) : null; patientName.Text = patient?.FullName ?? ""; nationalId.Text = patient?.NationalId ?? ""; mobile.Text = patient?.Mobile ?? ""; }; file.LostFocus += delegate { resolve(); }; resolve();
            Appointment saved = null; ContentDialog dialog = owner.Dialog(original == null ? "موعد جديد" : "تعديل الموعد", form.Scroll(), "حفظ", "إلغاء"); dialog.PrimaryButtonClick += delegate(ContentDialog sender, ContentDialogButtonClickEventArgs args)
            {
                try { resolve(); if (patient == null) throw new InvalidOperationException("لا يوجد مراجع نشط بهذا الرقم."); if (string.IsNullOrWhiteSpace(title.Text)) throw new InvalidOperationException("أدخل عنوان الموعد."); int minutes = int.Parse(Convert.ToString(duration.SelectedItem)); DateTime value = when.Value; if (!SaudiValidation.IsOfficialWorkingDay(value)) throw new InvalidOperationException("لا تُقبل المواعيد يوم الجمعة أو السبت."); Appointment a = Copy(original); a.PatientId = patient.Id; a.FileNumber = patient.FileNumber; a.PatientName = patient.FullName; a.Title = title.Text.Trim(); a.VisitType = Convert.ToString(type.SelectedItem); a.StartsAt = value; a.DurationMinutes = minutes; a.Status = Convert.ToString(status.SelectedItem); a.Notes = notes.Text.Trim(); saved = original == null ? database.AddAppointment(a) : SaveAppointment(database, a); form.ClearError(); } catch (Exception ex) { args.Cancel = true; form.ShowError(ex.Message); }
            }; await dialog.ShowAsync(); return saved;
        }

        public static async Task<PatientTask> EditTaskAsync(MainWindow owner, AppDatabase database, PatientTask original, long? initialFileNumber)
        {
            AppSettings settings = database.GetSettings(); var form = new FormHost(); var file = form.Text("رقم ملف المراجع", original != null ? original.FileNumber.ToString() : initialFileNumber?.ToString(), false, 12); var patientName = form.Text("اسم المراجع", original?.PatientName, true); var title = form.Text("اسم المهمة/التنبيه", original?.Title, false, 150); var due = form.Date("وقت الاستحقاق الميلادي", original?.DueAt ?? DateTime.Now.AddHours(1), true); var priority = form.Combo("الأولوية", settings.TaskPriorities, original?.Priority); var completion = form.Combo("الحالة", new[] { "مفتوحة", "مكتملة" }, original != null && original.IsCompleted ? "مكتملة" : "مفتوحة"); var notes = form.Text("ملاحظات", original?.Notes, false, 1000, true); Patient patient = null; Action resolve = delegate { long n; patient = long.TryParse(SaudiValidation.NormalizeDigits(file.Text), out n) ? database.FindByFileNumber(n, false) : null; patientName.Text = patient?.FullName ?? ""; }; file.LostFocus += delegate { resolve(); }; resolve();
            PatientTask saved = null; ContentDialog dialog = owner.Dialog(original == null ? "مهمة/تنبيه جديد" : "تعديل المهمة/التنبيه", form.Scroll(), "حفظ", "إلغاء"); dialog.PrimaryButtonClick += delegate(ContentDialog sender, ContentDialogButtonClickEventArgs args)
            {
                try { resolve(); if (patient == null) throw new InvalidOperationException("لا يوجد مراجع نشط بهذا الرقم."); if (string.IsNullOrWhiteSpace(title.Text)) throw new InvalidOperationException("أدخل اسم المهمة أو التنبيه."); DateTime value = due.Value; if (original == null && value < DateTime.Now.AddMinutes(-1)) throw new InvalidOperationException("لا يمكن إنشاء مهمة بوقت سابق."); PatientTask t = Copy(original); t.PatientId = patient.Id; t.FileNumber = patient.FileNumber; t.PatientName = patient.FullName; t.Title = title.Text.Trim(); t.DueAt = value; t.Priority = Convert.ToString(priority.SelectedItem); t.IsCompleted = Convert.ToString(completion.SelectedItem) == "مكتملة"; t.Notes = notes.Text.Trim(); saved = original == null ? database.AddTask(t) : SaveTask(database, t); form.ClearError(); } catch (Exception ex) { args.Cancel = true; form.ShowError(ex.Message); }
            }; await dialog.ShowAsync(); return saved;
        }

        private static Patient SavePatient(AppDatabase database, Patient patient) { database.UpdatePatient(patient); return patient; }
        private static Appointment SaveAppointment(AppDatabase database, Appointment appointment) { database.UpdateAppointment(appointment); return appointment; }
        private static PatientTask SaveTask(AppDatabase database, PatientTask task) { database.UpdateTask(task); return task; }
        private static Patient Copy(Patient p) { return p == null ? new Patient() : new Patient { Id = p.Id, FileNumber = p.FileNumber, NormalizedName = p.NormalizedName, CreatedAt = p.CreatedAt, UpdatedAt = p.UpdatedAt, LastVisitAt = p.LastVisitAt, IsArchived = p.IsArchived, ArchivedAt = p.ArchivedAt, ArchiveReason = p.ArchiveReason }; }
        private static Appointment Copy(Appointment a) { return a == null ? new Appointment() : new Appointment { Id = a.Id, CreatedAt = a.CreatedAt, UpdatedAt = a.UpdatedAt, ReminderNotifiedAt = a.ReminderNotifiedAt, IsDeleted = a.IsDeleted, DeletedAt = a.DeletedAt, DeletedBy = a.DeletedBy }; }
        private static PatientTask Copy(PatientTask t) { return t == null ? new PatientTask() : new PatientTask { Id = t.Id, CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt, ReminderNotifiedAt = t.ReminderNotifiedAt, IsDeleted = t.IsDeleted, DeletedAt = t.DeletedAt, DeletedBy = t.DeletedBy }; }

        private sealed class FormHost
        {
            public StackPanel Panel { get; } = new StackPanel { Spacing = 8, MinWidth = 560, MaxWidth = 720 };
            private readonly TextBlock error = new TextBlock { Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Firebrick), TextWrapping = TextWrapping.Wrap };
            public FormHost() { Panel.Children.Add(error); }
            public TextBox Text(string header, string value, bool readOnly, int maxLength = 0, bool multiline = false) { var c = new TextBox { Header = header, Text = value ?? "", IsReadOnly = readOnly, MaxLength = maxLength, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 84 : 0 }; Panel.Children.Add(c); return c; }
            public ComboBox Combo(string header, IEnumerable<string> values, string selected) { var c = new ComboBox { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch }; foreach (string v in values ?? Enumerable.Empty<string>()) c.Items.Add(v); if (!string.IsNullOrWhiteSpace(selected) && !c.Items.Contains(selected)) c.Items.Add(selected); if (!string.IsNullOrWhiteSpace(selected)) c.SelectedItem = selected; else if (c.Items.Count > 0) c.SelectedIndex = 0; Panel.Children.Add(c); return c; }
            public DateTimeSpinner Date(string header, DateTime value, bool withTime) { Panel.Children.Add(new TextBlock { Text = header, FontWeight = Windows.UI.Text.FontWeights.SemiBold }); var c = new DateTimeSpinner(withTime) { Value = value }; Panel.Children.Add(c); return c; }
            public ScrollViewer Scroll() { return new ScrollViewer { Content = Panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 680 }; }
            public void ShowError(string value) { error.Text = value; }
            public void ClearError() { error.Text = ""; }
        }

        private sealed class DateTimeSpinner : Grid
        {
            private readonly ComboBox year = new ComboBox(), month = new ComboBox(), day = new ComboBox(), hour = new ComboBox(), minute = new ComboBox(); private readonly bool withTime; private bool changing;
            public DateTimeSpinner(bool withTime)
            {
                this.withTime = withTime; ColumnSpacing = 6; int columns = withTime ? 5 : 3; for (int i = 0; i < columns; i++) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); for (int y = DateTime.Today.Year - 10; y <= DateTime.Today.Year + 15; y++) year.Items.Add(y); for (int m = 1; m <= 12; m++) month.Items.Add(new MonthItem(m)); for (int h = 0; h < 24; h++) hour.Items.Add(h.ToString("00")); for (int n = 0; n < 60; n += 5) minute.Items.Add(n.ToString("00")); Add(year, 0, "السنة"); Add(month, 1, "الشهر"); Add(day, 2, "اليوم"); if (withTime) { Add(hour, 3, "الساعة"); Add(minute, 4, "الدقيقة"); } year.SelectionChanged += delegate { RefreshDays(); }; month.SelectionChanged += delegate { RefreshDays(); };
            }
            public DateTime Value { get { int h = withTime ? int.Parse(Convert.ToString(hour.SelectedItem)) : 0, n = withTime ? int.Parse(Convert.ToString(minute.SelectedItem)) : 0; return new DateTime((int)year.SelectedItem, ((MonthItem)month.SelectedItem).Number, (int)day.SelectedItem, h, n, 0); } set { changing = true; if (!year.Items.Contains(value.Year)) year.Items.Add(value.Year); year.SelectedItem = value.Year; month.SelectedIndex = value.Month - 1; FillDays(value.Day); if (withTime) { hour.SelectedItem = value.Hour.ToString("00"); minute.SelectedItem = (Math.Min(55, value.Minute / 5 * 5)).ToString("00"); } changing = false; } }
            private void RefreshDays() { if (!changing && year.SelectedItem != null && month.SelectedItem != null) FillDays(day.SelectedItem == null ? 1 : (int)day.SelectedItem); }
            private void FillDays(int selected) { if (year.SelectedItem == null || month.SelectedItem == null) return; int count = DateTime.DaysInMonth((int)year.SelectedItem, ((MonthItem)month.SelectedItem).Number); day.Items.Clear(); for (int d = 1; d <= count; d++) day.Items.Add(d); day.SelectedItem = Math.Min(selected, count); }
            private void Add(ComboBox combo, int column, string header) { combo.Header = header; combo.HorizontalAlignment = HorizontalAlignment.Stretch; SetColumn(combo, column); Children.Add(combo); }
            private sealed class MonthItem { public int Number { get; } public MonthItem(int number) { Number = number; } public override string ToString() { return SaudiValidation.MonthLabel(Number); } }
        }
    }
}
