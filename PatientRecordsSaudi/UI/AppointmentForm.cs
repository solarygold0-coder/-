using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.UI
{
    public sealed class AppointmentForm : Form
    {
        private readonly AppDatabase database; private readonly Appointment original; private Patient selectedPatient;
        private readonly TextBox fileNumber = UiKit.TextBox(12), patientName = UiKit.TextBox(150), nationalId = UiKit.TextBox(10), mobile = UiKit.TextBox(16), title = UiKit.TextBox(150), notes = UiKit.TextBox(1000);
        private readonly ComboBox visitType = UiKit.Combo(), status = UiKit.Combo(), duration = UiKit.Combo("15", "30", "45", "60", "90", "120");
        private readonly DateTimeScrollControl dateTime = new DateTimeScrollControl(true);
        public Appointment Result { get; private set; }

        public AppointmentForm(AppDatabase database, Appointment appointment, long? initialFileNumber)
        {
            this.database = database; original = appointment;
            AppSettings settings = database.GetSettings(); visitType.Items.AddRange(settings.VisitTypes.ToArray()); status.Items.AddRange(settings.AppointmentStatuses.ToArray()); if (visitType.Items.Count > 0) visitType.SelectedIndex = 0; if (status.Items.Count > 0) status.SelectedIndex = 0;
            Text = appointment == null ? "موعد جديد" : "تعديل الموعد"; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; Font = UiKit.NormalFont;
            StartPosition = FormStartPosition.CenterParent; Size = new Size(720, 620); MinimumSize = new Size(650, 560); BackColor = UiKit.Background;
            var head = new Label { Text = Text, Dock = DockStyle.Top, Height = 50, BackColor = UiKit.Primary, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Tahoma", 14, FontStyle.Bold) }; Controls.Add(head);
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, AutoScroll = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
            Add(table, "رقم ملف المراجع", fileNumber); Add(table, "اسم المراجع", patientName); Add(table, "الهوية/الإقامة", nationalId); Add(table, "الجوال", mobile);
            Add(table, "عنوان الموعد", title); Add(table, "نوع الزيارة", visitType); Add(table, "التاريخ والوقت الميلادي", dateTime); Add(table, "المدة بالدقائق", duration); Add(table, "الحالة", status);
            notes.Multiline = true; notes.Height = 80; notes.ScrollBars = ScrollBars.Vertical; Add(table, "ملاحظات", notes); Controls.Add(table);
            patientName.ReadOnly = nationalId.ReadOnly = mobile.ReadOnly = true; patientName.BackColor = nationalId.BackColor = mobile.BackColor = Color.FromArgb(235, 238, 240);
            fileNumber.KeyPress += delegate(object s, KeyPressEventArgs e) { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            fileNumber.Leave += delegate { ResolvePatient(true); }; int defaultMinutes = database.GetSettings().DefaultAppointmentMinutes; duration.SelectedItem = defaultMinutes.ToString(); if (appointment == null) dateTime.Value = database.GetNextAvailableAppointmentTime(defaultMinutes);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
            buttons.Controls.Add(UiKit.Button("حفظ الموعد", Save, false)); buttons.Controls.Add(UiKit.Button("إلغاء", delegate { DialogResult = DialogResult.Cancel; Close(); }, true)); Controls.Add(buttons);
            if (appointment != null) LoadAppointment(appointment); else if (initialFileNumber.HasValue) { fileNumber.Text = initialFileNumber.Value.ToString(); ResolvePatient(false); }
        }
        private static void Add(TableLayoutPanel t, string label, Control c) { int r = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.Controls.Add(UiKit.Label(label, true), 0, r); t.Controls.Add(c, 1, r); }
        private bool ResolvePatient(bool showError)
        {
            long number; selectedPatient = null; patientName.Clear(); nationalId.Clear(); mobile.Clear();
            if (!long.TryParse(SaudiValidation.NormalizeDigits(fileNumber.Text), out number)) { if (showError) UiKit.ShowError("أدخل رقم ملف صحيحًا بالأرقام فقط."); return false; }
            selectedPatient = database.FindByFileNumber(number, false);
            if (selectedPatient == null) { if (showError) UiKit.ShowError("لا يوجد مراجع نشط بهذا الرقم. تحقق من رقم الملف."); return false; }
            patientName.Text = selectedPatient.FullName; nationalId.Text = selectedPatient.NationalId; mobile.Text = selectedPatient.Mobile; return true;
        }
        private void Save(object sender, EventArgs e)
        {
            if (!ResolvePatient(true)) { fileNumber.Focus(); return; }
            if (string.IsNullOrWhiteSpace(title.Text)) { UiKit.ShowError("أدخل عنوان الموعد."); title.Focus(); return; }
            DateTime value = dateTime.Value;
            if (!SaudiValidation.IsOfficialWorkingDay(value)) { UiKit.ShowError("لا تُقبل المواعيد يوم الجمعة أو السبت. اختر يومًا من الأحد إلى الخميس."); return; }
            int minutes; if (!int.TryParse(duration.Text, out minutes) || minutes < 5) { UiKit.ShowError("مدة الموعد غير صحيحة."); return; }
            Result = original ?? new Appointment(); Result.PatientId = selectedPatient.Id; Result.FileNumber = selectedPatient.FileNumber; Result.PatientName = selectedPatient.FullName;
            Result.Title = title.Text.Trim(); Result.VisitType = visitType.Text; Result.StartsAt = value; Result.DurationMinutes = minutes; Result.Status = status.Text; Result.Notes = notes.Text.Trim();
            try { database.ValidateAppointmentAvailability(Result); }
            catch (Exception ex) { UiKit.ShowError(ex.Message); return; }
            DialogResult = DialogResult.OK; Close();
        }
        private void LoadAppointment(Appointment a)
        {
            if (!visitType.Items.Contains(a.VisitType)) visitType.Items.Add(a.VisitType); if (!status.Items.Contains(a.Status)) status.Items.Add(a.Status);
            fileNumber.Text = a.FileNumber.ToString(); ResolvePatient(false); title.Text = a.Title; visitType.SelectedItem = a.VisitType; dateTime.Value = a.StartsAt;
            duration.SelectedItem = a.DurationMinutes.ToString(); status.SelectedItem = a.Status; notes.Text = a.Notes;
        }
    }
}
