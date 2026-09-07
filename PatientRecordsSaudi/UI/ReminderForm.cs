using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using PatientRecordsSaudi.Models;

namespace PatientRecordsSaudi.UI
{
    internal sealed class LockedReminderForm : Form
    {
        public LockedReminderForm(int appointmentCount, int taskCount, Action openApplication)
        {
            Text = "تنبيه صامت"; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog; ClientSize = new Size(460, 220); MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false; TopMost = true; Font = UiKit.NormalFont; BackColor = UiKit.Background;
            var title = new Label { Text = "تنبيهات تحتاج المراجعة", Dock = DockStyle.Top, Height = 54, BackColor = UiKit.Primary, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Tahoma", 13, FontStyle.Bold) };
            var summary = new Label { Text = "يوجد " + appointmentCount + " موعد قريب و" + taskCount + " مهمة مستحقة.\nسجل الدخول لعرض التفاصيل.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = UiKit.BoldFont };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
            buttons.Controls.Add(UiKit.Button("فتح البرنامج", delegate { if (openApplication != null) openApplication(); }, false));
            buttons.Controls.Add(UiKit.Button("لاحقًا", delegate { Close(); }, true)); Controls.Add(summary); Controls.Add(buttons); Controls.Add(title);
        }
    }

    public sealed class ReminderForm : Form
    {
        private readonly Action<Guid> openPatient;
        private readonly DataGridView appointmentGrid = UiKit.Grid(), taskGrid = UiKit.Grid();
        private readonly TabControl tabs = new TabControl();

        public ReminderForm(IList<Appointment> appointments, IList<PatientTask> tasks, Action<Guid> openPatient)
        {
            this.openPatient = openPatient;
            Text = "تنبيهات المواعيد والمهام"; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
            StartPosition = FormStartPosition.CenterParent; Size = new Size(940, 560); MinimumSize = new Size(760, 480);
            Font = UiKit.NormalFont; BackColor = UiKit.Background; ShowInTaskbar = false;

            var header = new Label
            {
                Text = "تنبيه صامت — المواعيد خلال اليومين القادمين والمهام المستحقة",
                Dock = DockStyle.Top, Height = 54, BackColor = UiKit.Primary, ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Tahoma", 13, FontStyle.Bold)
            };
            Controls.Add(header);

            ConfigureAppointmentGrid(); ConfigureTaskGrid();
            appointmentGrid.DataSource = new BindingList<Appointment>(new List<Appointment>(appointments ?? new List<Appointment>()));
            taskGrid.DataSource = new BindingList<PatientTask>(new List<PatientTask>(tasks ?? new List<PatientTask>()));

            TabPage appointmentTab = new TabPage("المواعيد (" + appointmentGrid.Rows.Count + ")") { Padding = new Padding(6) };
            TabPage taskTab = new TabPage("المهام (" + taskGrid.Rows.Count + ")") { Padding = new Padding(6) };
            appointmentTab.Controls.Add(appointmentGrid); taskTab.Controls.Add(taskGrid);
            tabs.Dock = DockStyle.Fill; tabs.Font = UiKit.BoldFont; tabs.TabPages.Add(appointmentTab); tabs.TabPages.Add(taskTab); Controls.Add(tabs);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
            buttons.Controls.Add(UiKit.Button("فتح ملف المراجع", delegate { OpenSelected(); }, false));
            buttons.Controls.Add(UiKit.Button("إغلاق", delegate { Close(); }, true)); Controls.Add(buttons);

            appointmentGrid.CellContentClick += OpenLinkedRow; taskGrid.CellContentClick += OpenLinkedRow;
            appointmentGrid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenFromRow(appointmentGrid.Rows[e.RowIndex]); };
            taskGrid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenFromRow(taskGrid.Rows[e.RowIndex]); };
        }

        private void ConfigureAppointmentGrid()
        {
            appointmentGrid.Columns.Add(new DataGridViewLinkColumn { DataPropertyName = "PatientName", Name = "PatientName", HeaderText = "اسم المراجع", FillWeight = 28, LinkColor = UiKit.Primary, TrackVisitedState = false });
            appointmentGrid.Columns.Add(new DataGridViewLinkColumn { DataPropertyName = "Title", Name = "Title", HeaderText = "الموعد", FillWeight = 26, LinkColor = UiKit.Primary, TrackVisitedState = false });
            UiKit.AddTextColumn(appointmentGrid, "DateText", "التاريخ الميلادي", 28); UiKit.AddTextColumn(appointmentGrid, "TimeText", "الوقت", 16);
            appointmentGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "PatientId", Name = "PatientId", Visible = false });
        }

        private void ConfigureTaskGrid()
        {
            taskGrid.Columns.Add(new DataGridViewLinkColumn { DataPropertyName = "PatientName", Name = "PatientName", HeaderText = "اسم المراجع", FillWeight = 30, LinkColor = UiKit.Primary, TrackVisitedState = false });
            taskGrid.Columns.Add(new DataGridViewLinkColumn { DataPropertyName = "Title", Name = "Title", HeaderText = "المهمة/التنبيه", FillWeight = 32, LinkColor = UiKit.Primary, TrackVisitedState = false });
            UiKit.AddTextColumn(taskGrid, "DueText", "موعد الاستحقاق", 26); UiKit.AddTextColumn(taskGrid, "Priority", "الأولوية", 12);
            taskGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "PatientId", Name = "PatientId", Visible = false });
        }

        private void OpenLinkedRow(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return; DataGridView grid = (DataGridView)sender; string name = grid.Columns[e.ColumnIndex].Name;
            if (name == "PatientName" || name == "Title") OpenFromRow(grid.Rows[e.RowIndex]);
        }

        private void OpenSelected()
        {
            DataGridView grid = tabs.SelectedIndex == 0 ? appointmentGrid : taskGrid;
            if (grid.CurrentRow == null) { UiKit.ShowError("اختر موعدًا أو مهمة أولًا."); return; } OpenFromRow(grid.CurrentRow);
        }

        private void OpenFromRow(DataGridViewRow row)
        {
            object value = row.Cells["PatientId"].Value; Guid patientId;
            if (value == null || !Guid.TryParse(value.ToString(), out patientId)) { UiKit.ShowError("تعذر تحديد ملف المراجع."); return; }
            if (openPatient != null) openPatient(patientId);
        }
    }
}
