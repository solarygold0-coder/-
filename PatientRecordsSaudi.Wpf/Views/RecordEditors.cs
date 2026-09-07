using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.Wpf.Views
{
    internal abstract class EditorWindowBase : Window
    {
        protected readonly StackPanel FormPanel = new StackPanel();
        private readonly DockPanel root = new DockPanel(); private readonly StackPanel buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 10, 4, 10) };
        protected EditorWindowBase(string title, double width, double height)
        {
            Title = title; Width = width; Height = height; MinWidth = Math.Min(width, 620); MinHeight = Math.Min(height, 520); WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var header = new Border { Background = (Brush)Application.Current.Resources["PrimaryBrush"], Padding = new Thickness(18) };
            header.Child = new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center };
            DockPanel.SetDock(header, Dock.Top); root.Children.Add(header); DockPanel.SetDock(buttonPanel, Dock.Bottom); root.Children.Add(buttonPanel);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = FormPanel, Margin = new Thickness(18) }; root.Children.Add(scroll); Content = root;
        }
        protected void AddField(string label, UIElement control)
        {
            FormPanel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 8, 4, 0) }); FormPanel.Children.Add(control);
        }
        protected void AddButtons(RoutedEventHandler save, bool readOnly)
        {
            if (!readOnly) { var button = new Button { Content = "حفظ", MinWidth = 130, IsDefault = true }; button.Click += save; buttonPanel.Children.Add(button); }
            var close = new Button { Content = readOnly ? "إغلاق" : "إلغاء", MinWidth = 110, IsCancel = true }; close.SetResourceReference(StyleProperty, "DangerButton"); close.Click += delegate { DialogResult = false; }; buttonPanel.Children.Add(close);
        }
        protected static TextBox Box(int maxLength, bool multiline = false)
        {
            return new TextBox { MaxLength = maxLength, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, Height = multiline ? 72 : double.NaN, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden };
        }
        protected static ComboBox Choices(IEnumerable<string> values) { var c = new ComboBox(); foreach (string value in values) c.Items.Add(value); if (c.Items.Count > 0) c.SelectedIndex = 0; return c; }
        protected static void DigitsOnly(TextBox box)
        {
            box.PreviewTextInput += delegate(object sender, TextCompositionEventArgs e) { e.Handled = e.Text.Any(ch => !char.IsDigit(ch)); };
            DataObject.AddPastingHandler(box, delegate(object sender, DataObjectPastingEventArgs e) { string value = e.DataObject.GetData(typeof(string)) as string; if (value == null || value.Any(ch => !char.IsDigit(ch))) e.CancelCommand(); });
        }
        protected static void Error(string message) { MessageBox.Show(message, "تحقق من البيانات", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    internal sealed class PatientEditorWindow : EditorWindowBase
    {
        private readonly Patient original; private readonly bool readOnly;
        private readonly ComboBox identityType, gender, blood; private readonly TextBox nationalId = Box(10), fullName = Box(150), nationality = Box(80), mobile = Box(16), alternate = Box(16), city = Box(80), address = Box(250), emergencyName = Box(120), emergencyPhone = Box(16), allergies = Box(1000, true), chronic = Box(1000, true), notes = Box(2000, true); private readonly DatePicker birth = new DatePicker();
        public Patient Result { get; private set; }
        public PatientEditorWindow(AppDatabase database, Patient patient, bool readOnly) : base(patient == null ? "إضافة مراجع جديد" : (readOnly ? "عرض ملف المراجع" : "تعديل ملف المراجع"), 760, 860)
        {
            original = patient; this.readOnly = readOnly; AppSettings s = database.GetSettings(); identityType = Choices(new[] { "هوية وطنية", "إقامة" }); gender = Choices(s.GenderOptions); blood = Choices(s.BloodTypes);
            if (patient != null) AddField("رقم الملف", new TextBox { Text = patient.FileNumber.ToString(), IsReadOnly = true, Background = Brushes.Gainsboro, FlowDirection = FlowDirection.LeftToRight });
            else AddField("رقم الملف", new TextBox { Text = "يُنشأ تلقائيًا عند الحفظ", IsReadOnly = true, Background = Brushes.Gainsboro });
            AddField("نوع الهوية", identityType); AddField("رقم الهوية/الإقامة — أرقام فقط", nationalId); AddField("الاسم الكامل", fullName); AddField("الجنس", gender); AddField("تاريخ الميلاد الميلادي", birth); AddField("الجنسية", nationality); AddField("الجوال السعودي", mobile); AddField("هاتف بديل", alternate); AddField("المدينة", city); AddField("العنوان", address); AddField("اسم جهة اتصال الطوارئ", emergencyName); AddField("جوال الطوارئ", emergencyPhone); AddField("فصيلة الدم", blood); AddField("الحساسية", allergies); AddField("الأمراض المزمنة", chronic); AddField("ملاحظات إدارية", notes);
            DigitsOnly(nationalId); AddPhoneFilter(mobile); AddPhoneFilter(alternate); AddPhoneFilter(emergencyPhone); birth.SelectedDate = new DateTime(1990, 1, 1); nationality.Text = "سعودي";
            if (patient != null) Load(patient); if (readOnly) FormPanel.IsEnabled = false; AddButtons(Save_Click, readOnly);
        }
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string validation; if (!SaudiValidation.ValidateSaudiIdentity(nationalId.Text, Convert.ToString(identityType.SelectedItem), out validation)) { Error(validation); nationalId.Focus(); return; }
            if (string.IsNullOrWhiteSpace(fullName.Text) || fullName.Text.Trim().Length < 4 || !fullName.Text.Any(char.IsLetter)) { Error("أدخل الاسم الكامل بصورة صحيحة."); fullName.Focus(); return; }
            if (!birth.SelectedDate.HasValue || birth.SelectedDate.Value.Date > DateTime.Today) { Error("تاريخ الميلاد الميلادي غير صحيح."); return; }
            if (!SaudiValidation.ValidateSaudiMobile(mobile.Text, true, out validation)) { Error(validation); mobile.Focus(); return; }
            if (!SaudiValidation.ValidateSaudiMobile(alternate.Text, false, out validation)) { Error("الهاتف البديل: " + validation); return; }
            if (!SaudiValidation.ValidateSaudiMobile(emergencyPhone.Text, false, out validation)) { Error("جوال الطوارئ: " + validation); return; }
            if (string.IsNullOrWhiteSpace(city.Text)) { Error("المدينة مطلوبة."); city.Focus(); return; }
            Result = CopyOriginal(); Result.IdentityType = Convert.ToString(identityType.SelectedItem); Result.NationalId = SaudiValidation.NormalizeDigits(nationalId.Text); Result.FullName = fullName.Text.Trim(); Result.Gender = Convert.ToString(gender.SelectedItem); Result.DateOfBirth = birth.SelectedDate.Value.Date; Result.Nationality = nationality.Text.Trim(); Result.Mobile = SaudiValidation.NormalizeSaudiMobile(mobile.Text); Result.AlternatePhone = SaudiValidation.NormalizeSaudiMobile(alternate.Text); Result.City = city.Text.Trim(); Result.Address = address.Text.Trim(); Result.EmergencyContact = emergencyName.Text.Trim(); Result.EmergencyPhone = SaudiValidation.NormalizeSaudiMobile(emergencyPhone.Text); Result.BloodType = Convert.ToString(blood.SelectedItem); Result.Allergies = allergies.Text.Trim(); Result.ChronicConditions = chronic.Text.Trim(); Result.Notes = notes.Text.Trim(); DialogResult = true;
        }
        private Patient CopyOriginal()
        {
            if (original == null) return new Patient(); return new Patient { Id = original.Id, FileNumber = original.FileNumber, NormalizedName = original.NormalizedName, CreatedAt = original.CreatedAt, UpdatedAt = original.UpdatedAt, LastVisitAt = original.LastVisitAt, IsArchived = original.IsArchived, ArchivedAt = original.ArchivedAt, ArchiveReason = original.ArchiveReason };
        }
        private void Load(Patient p)
        {
            identityType.SelectedItem = p.IdentityType; nationalId.Text = p.NationalId; fullName.Text = p.FullName; gender.SelectedItem = p.Gender; birth.SelectedDate = p.DateOfBirth; nationality.Text = p.Nationality; mobile.Text = p.Mobile; alternate.Text = p.AlternatePhone; city.Text = p.City; address.Text = p.Address; emergencyName.Text = p.EmergencyContact; emergencyPhone.Text = p.EmergencyPhone; blood.SelectedItem = p.BloodType; allergies.Text = p.Allergies; chronic.Text = p.ChronicConditions; notes.Text = p.Notes;
        }
        private static void AddPhoneFilter(TextBox box) { box.PreviewTextInput += delegate(object sender, TextCompositionEventArgs e) { e.Handled = e.Text.Any(ch => !char.IsDigit(ch) && ch != '+' && ch != '-' && ch != ' '); }; }
    }

    internal sealed class AppointmentEditorWindow : EditorWindowBase
    {
        private readonly AppDatabase database; private readonly Appointment original; private Patient patient;
        private readonly TextBox fileNumber = Box(12), patientName = Box(150), nationalId = Box(10), mobile = Box(16), title = Box(150), notes = Box(1000, true); private readonly ComboBox visitType, duration, status; private readonly DateTimeScrollControl when = new DateTimeScrollControl();
        public Appointment Result { get; private set; }
        public AppointmentEditorWindow(AppDatabase database, Appointment appointment, long? initialFileNumber) : base(appointment == null ? "موعد جديد" : "تعديل الموعد", 720, 760)
        {
            this.database = database; original = appointment; AppSettings s = database.GetSettings(); visitType = Choices(s.VisitTypes); duration = Choices(new[] { "15", "30", "45", "60", "90", "120" }); status = Choices(s.AppointmentStatuses);
            AddField("رقم ملف المراجع — أرقام فقط", fileNumber); AddField("اسم المراجع", patientName); AddField("الهوية/الإقامة", nationalId); AddField("الجوال", mobile); AddField("عنوان الموعد", title); AddField("نوع الزيارة", visitType); AddField("التاريخ والوقت الميلادي — مرّر القوائم بعجلة الفأرة", when); AddField("المدة بالدقائق", duration); AddField("الحالة", status); AddField("ملاحظات", notes);
            patientName.IsReadOnly = nationalId.IsReadOnly = mobile.IsReadOnly = true; patientName.Background = nationalId.Background = mobile.Background = Brushes.Gainsboro; DigitsOnly(fileNumber); fileNumber.LostFocus += delegate { Resolve(true); }; duration.SelectedItem = s.DefaultAppointmentMinutes.ToString();
            if (appointment != null) Load(appointment); else { when.Value = database.GetNextAvailableAppointmentTime(s.DefaultAppointmentMinutes); if (initialFileNumber.HasValue) { fileNumber.Text = initialFileNumber.Value.ToString(); Resolve(false); } }
            AddButtons(Save_Click, false);
        }
        private bool Resolve(bool show)
        {
            patient = null; patientName.Clear(); nationalId.Clear(); mobile.Clear(); long number; if (!long.TryParse(SaudiValidation.NormalizeDigits(fileNumber.Text), out number)) { if (show) Error("أدخل رقم ملف صحيحًا بالأرقام فقط."); return false; }
            patient = database.FindByFileNumber(number, false); if (patient == null) { if (show) Error("لا يوجد مراجع نشط بهذا الرقم."); return false; } patientName.Text = patient.FullName; nationalId.Text = patient.NationalId; mobile.Text = patient.Mobile; return true;
        }
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Resolve(true)) { fileNumber.Focus(); return; } if (string.IsNullOrWhiteSpace(title.Text)) { Error("أدخل عنوان الموعد."); return; } int minutes; if (!int.TryParse(Convert.ToString(duration.SelectedItem), out minutes)) { Error("مدة الموعد غير صحيحة."); return; }
            DateTime value; try { value = when.Value; } catch { Error("التاريخ أو الوقت غير صحيح."); return; } if (!SaudiValidation.IsOfficialWorkingDay(value)) { Error("لا تُقبل المواعيد يوم الجمعة أو السبت."); return; }
            Result = CopyOriginal(); Result.PatientId = patient.Id; Result.FileNumber = patient.FileNumber; Result.PatientName = patient.FullName; Result.Title = title.Text.Trim(); Result.VisitType = Convert.ToString(visitType.SelectedItem); Result.StartsAt = value; Result.DurationMinutes = minutes; Result.Status = Convert.ToString(status.SelectedItem); Result.Notes = notes.Text.Trim();
            try { database.ValidateAppointmentAvailability(Result); } catch (Exception ex) { Error(ex.Message); return; } DialogResult = true;
        }
        private Appointment CopyOriginal() { if (original == null) return new Appointment(); return new Appointment { Id = original.Id, CreatedAt = original.CreatedAt, UpdatedAt = original.UpdatedAt, ReminderNotifiedAt = original.ReminderNotifiedAt, IsDeleted = original.IsDeleted, DeletedAt = original.DeletedAt, DeletedBy = original.DeletedBy }; }
        private void Load(Appointment a) { fileNumber.Text = a.FileNumber.ToString(); Resolve(false); title.Text = a.Title; EnsureChoice(visitType, a.VisitType); when.Value = a.StartsAt; EnsureChoice(duration, a.DurationMinutes.ToString()); EnsureChoice(status, a.Status); notes.Text = a.Notes; }
        private static void EnsureChoice(ComboBox combo, string value) { if (!combo.Items.Contains(value)) combo.Items.Add(value); combo.SelectedItem = value; }
    }

    internal sealed class TaskEditorWindow : EditorWindowBase
    {
        private readonly AppDatabase database; private readonly PatientTask original; private Patient patient; private readonly TextBox fileNumber = Box(12), patientName = Box(150), title = Box(150), notes = Box(1000, true); private readonly ComboBox priority, completion; private readonly DateTimeScrollControl due = new DateTimeScrollControl();
        public PatientTask Result { get; private set; }
        public TaskEditorWindow(AppDatabase database, PatientTask task, long? initialFileNumber) : base(task == null ? "مهمة/تنبيه جديد" : "تعديل المهمة/التنبيه", 680, 680)
        {
            this.database = database; original = task; priority = Choices(database.GetSettings().TaskPriorities); completion = Choices(new[] { "مفتوحة", "مكتملة" }); AddField("رقم ملف المراجع", fileNumber); AddField("اسم المراجع", patientName); AddField("اسم المهمة/التنبيه", title); AddField("وقت الاستحقاق الميلادي", due); AddField("الأولوية", priority); AddField("الحالة", completion); AddField("ملاحظات", notes); patientName.IsReadOnly = true; patientName.Background = Brushes.Gainsboro; DigitsOnly(fileNumber); fileNumber.LostFocus += delegate { Resolve(true); }; due.Value = DateTime.Now.AddHours(1);
            if (task != null) Load(task); else if (initialFileNumber.HasValue) { fileNumber.Text = initialFileNumber.Value.ToString(); Resolve(false); } AddButtons(Save_Click, false);
        }
        private bool Resolve(bool show) { patient = null; patientName.Clear(); long n; if (!long.TryParse(SaudiValidation.NormalizeDigits(fileNumber.Text), out n)) { if (show) Error("أدخل رقم ملف صحيحًا."); return false; } patient = database.FindByFileNumber(n, false); if (patient == null) { if (show) Error("لا يوجد مراجع نشط بهذا الرقم."); return false; } patientName.Text = patient.FullName; return true; }
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Resolve(true)) return; if (string.IsNullOrWhiteSpace(title.Text)) { Error("أدخل اسم المهمة أو التنبيه."); return; } DateTime value; try { value = due.Value; } catch { Error("وقت الاستحقاق غير صحيح."); return; } if (original == null && value < DateTime.Now.AddMinutes(-1)) { Error("لا يمكن إنشاء مهمة جديدة بوقت سابق."); return; }
            Result = CopyOriginal(); Result.PatientId = patient.Id; Result.FileNumber = patient.FileNumber; Result.PatientName = patient.FullName; Result.Title = title.Text.Trim(); Result.DueAt = value; Result.Priority = Convert.ToString(priority.SelectedItem); Result.IsCompleted = Convert.ToString(completion.SelectedItem) == "مكتملة"; Result.Notes = notes.Text.Trim(); DialogResult = true;
        }
        private PatientTask CopyOriginal() { if (original == null) return new PatientTask(); return new PatientTask { Id = original.Id, CreatedAt = original.CreatedAt, UpdatedAt = original.UpdatedAt, ReminderNotifiedAt = original.ReminderNotifiedAt, IsDeleted = original.IsDeleted, DeletedAt = original.DeletedAt, DeletedBy = original.DeletedBy }; }
        private void Load(PatientTask t) { fileNumber.Text = t.FileNumber.ToString(); Resolve(false); title.Text = t.Title; if (!priority.Items.Contains(t.Priority)) priority.Items.Add(t.Priority); priority.SelectedItem = t.Priority; due.Value = t.DueAt; completion.SelectedItem = t.IsCompleted ? "مكتملة" : "مفتوحة"; notes.Text = t.Notes; }
    }

    internal sealed class DateTimeScrollControl : Border
    {
        private readonly ComboBox year = new ComboBox(), month = new ComboBox(), day = new ComboBox(), hour = new ComboBox(), minute = new ComboBox(); private bool loading;
        public DateTimeScrollControl()
        {
            BorderBrush = Brushes.LightGray; BorderThickness = new Thickness(1); Padding = new Thickness(7); CornerRadius = new CornerRadius(3); var panel = new WrapPanel { FlowDirection = FlowDirection.RightToLeft };
            for (int y = DateTime.Today.Year - 10; y <= DateTime.Today.Year + 15; y++) year.Items.Add(y); for (int m = 1; m <= 12; m++) month.Items.Add(new MonthChoice(m)); for (int h = 0; h < 24; h++) hour.Items.Add(h.ToString("00")); for (int n = 0; n < 60; n += 5) minute.Items.Add(n.ToString("00"));
            panel.Children.Add(Labeled("السنة", year, 90)); panel.Children.Add(Labeled("الشهر", month, 150)); panel.Children.Add(Labeled("اليوم", day, 75)); panel.Children.Add(Labeled("الساعة", hour, 75)); panel.Children.Add(Labeled("الدقيقة", minute, 75)); Child = panel;
            year.SelectionChanged += delegate { RefreshDays(); }; month.SelectionChanged += delegate { RefreshDays(); }; Value = DateTime.Now;
        }
        public DateTime Value
        {
            get { int y = (int)year.SelectedItem, m = ((MonthChoice)month.SelectedItem).Number, d = (int)day.SelectedItem, h = int.Parse(Convert.ToString(hour.SelectedItem)), n = int.Parse(Convert.ToString(minute.SelectedItem)); return new DateTime(y, m, d, h, n, 0); }
            set { loading = true; EnsureYear(value.Year); year.SelectedItem = value.Year; month.SelectedIndex = value.Month - 1; RefreshDays(value.Day); day.SelectedItem = value.Day; hour.SelectedItem = value.Hour.ToString("00"); int rounded = Math.Min(55, (value.Minute / 5) * 5); minute.SelectedItem = rounded.ToString("00"); loading = false; }
        }
        private void EnsureYear(int value) { if (!year.Items.Contains(value)) year.Items.Add(value); }
        private void RefreshDays() { if (loading || year.SelectedItem == null || month.SelectedItem == null) return; int selected = day.SelectedItem == null ? 1 : (int)day.SelectedItem; RefreshDays(selected); }
        private void RefreshDays(int desired) { if (year.SelectedItem == null || month.SelectedItem == null) return; int count = DateTime.DaysInMonth((int)year.SelectedItem, ((MonthChoice)month.SelectedItem).Number); day.Items.Clear(); for (int d = 1; d <= count; d++) day.Items.Add(d); day.SelectedItem = Math.Min(desired, count); }
        private static FrameworkElement Labeled(string label, ComboBox combo, double width) { combo.Width = width; var panel = new StackPanel { Margin = new Thickness(2) }; panel.Children.Add(new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }); panel.Children.Add(combo); return panel; }
        private sealed class MonthChoice { public int Number { get; private set; } public MonthChoice(int number) { Number = number; } public override string ToString() { return SaudiValidation.MonthLabel(Number); } }
    }
}
