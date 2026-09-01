using System;
using System.Drawing;
using System.Windows.Forms;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.UI
{
    public sealed class TaskForm : Form
    {
        private readonly AppDatabase database; private readonly PatientTask original; private Patient patient;
        private readonly TextBox fileNumber = UiKit.TextBox(12), patientName = UiKit.TextBox(150), title = UiKit.TextBox(150), notes = UiKit.TextBox(1000);
        private readonly ComboBox priority = UiKit.Combo("عادية", "عالية", "عاجلة"), completed = UiKit.Combo("مفتوحة", "مكتملة");
        private readonly DateTimeScrollControl due = new DateTimeScrollControl(true);
        public PatientTask Result { get; private set; }
        public TaskForm(AppDatabase database, PatientTask task, long? initialFileNumber)
        {
            this.database = database; original = task; Text = task == null ? "مهمة/تنبيه جديد" : "تعديل المهمة/التنبيه";
            RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; Font = UiKit.NormalFont; StartPosition = FormStartPosition.CenterParent; Size = new Size(680, 500); BackColor = UiKit.Background;
            Controls.Add(new Label { Text = Text, Dock = DockStyle.Top, Height = 50, BackColor = UiKit.Primary, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Tahoma", 14, FontStyle.Bold) });
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, AutoScroll = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
            Add(table, "رقم ملف المراجع", fileNumber); Add(table, "اسم المراجع", patientName); Add(table, "اسم المهمة/التنبيه", title); Add(table, "الموعد الميلادي", due); Add(table, "الأولوية", priority); Add(table, "الحالة", completed);
            notes.Multiline = true; notes.Height = 80; notes.ScrollBars = ScrollBars.Vertical; Add(table, "ملاحظات", notes); Controls.Add(table);
            patientName.ReadOnly = true; patientName.BackColor = Color.FromArgb(235, 238, 240); fileNumber.KeyPress += delegate(object s, KeyPressEventArgs e) { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; }; fileNumber.Leave += delegate { Resolve(true); };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
            buttons.Controls.Add(UiKit.Button("حفظ", Save, false)); buttons.Controls.Add(UiKit.Button("إلغاء", delegate { DialogResult = DialogResult.Cancel; Close(); }, true)); Controls.Add(buttons);
            if (task != null) LoadTask(task); else if (initialFileNumber.HasValue) { fileNumber.Text = initialFileNumber.Value.ToString(); Resolve(false); }
        }
        private static void Add(TableLayoutPanel t, string l, Control c) { int r = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.Controls.Add(UiKit.Label(l, true), 0, r); t.Controls.Add(c, 1, r); }
        private bool Resolve(bool show)
        {
            long n; patient = null; patientName.Clear(); if (!long.TryParse(SaudiValidation.NormalizeDigits(fileNumber.Text), out n)) { if (show) UiKit.ShowError("أدخل رقم ملف صحيحًا."); return false; }
            patient = database.FindByFileNumber(n, false); if (patient == null) { if (show) UiKit.ShowError("لا يوجد مراجع نشط بهذا الرقم."); return false; } patientName.Text = patient.FullName; return true;
        }
        private void Save(object sender, EventArgs e)
        {
            if (!Resolve(true)) return; if (string.IsNullOrWhiteSpace(title.Text)) { UiKit.ShowError("أدخل اسم المهمة أو التنبيه."); title.Focus(); return; }
            if (original == null && due.Value < DateTime.Now.AddMinutes(-1)) { UiKit.ShowError("لا يمكن إنشاء مهمة جديدة بوقت سابق."); return; }
            Result = original ?? new PatientTask(); Result.PatientId = patient.Id; Result.FileNumber = patient.FileNumber; Result.PatientName = patient.FullName; Result.Title = title.Text.Trim(); Result.DueAt = due.Value; Result.Priority = priority.Text; Result.IsCompleted = completed.SelectedIndex == 1; Result.Notes = notes.Text.Trim();
            DialogResult = DialogResult.OK; Close();
        }
        private void LoadTask(PatientTask t) { fileNumber.Text = t.FileNumber.ToString(); Resolve(false); title.Text = t.Title; due.Value = t.DueAt; priority.SelectedItem = t.Priority; completed.SelectedIndex = t.IsCompleted ? 1 : 0; notes.Text = t.Notes; }
    }
}
