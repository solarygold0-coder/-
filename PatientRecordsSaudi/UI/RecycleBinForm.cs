using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.UI
{
    public sealed class RecycleBinForm : Form
    {
        private readonly AppDatabase database; private readonly DataGridView appointments = UiKit.Grid(), tasks = UiKit.Grid();
        public RecycleBinForm(AppDatabase database)
        {
            this.database = database; Text = "المحذوفات القابلة للاستعادة"; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; StartPosition = FormStartPosition.CenterParent; Size = new Size(900, 600); Font = UiKit.NormalFont; BackColor = UiKit.Background;
            var tabs = new TabControl { Dock = DockStyle.Fill }; var a = new TabPage("المواعيد المحذوفة"), t = new TabPage("المهام المحذوفة"); ConfigureAppointmentGrid(); ConfigureTaskGrid(); a.Controls.Add(appointments); t.Controls.Add(tasks); tabs.TabPages.Add(a); tabs.TabPages.Add(t); Controls.Add(tabs);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) }; bottom.Controls.Add(UiKit.Button("استعادة المحدد", delegate { Restore(tabs.SelectedIndex); }, false)); Controls.Add(bottom); LoadData();
        }
        private void ConfigureAppointmentGrid() { UiKit.AddTextColumn(appointments, "FileNumber", "رقم الملف", 15); UiKit.AddTextColumn(appointments, "PatientName", "المراجع", 30); UiKit.AddTextColumn(appointments, "Title", "الموعد", 30); UiKit.AddTextColumn(appointments, "DeletedAt", "تاريخ الحذف", 25); }
        private void ConfigureTaskGrid() { UiKit.AddTextColumn(tasks, "FileNumber", "رقم الملف", 15); UiKit.AddTextColumn(tasks, "PatientName", "المراجع", 30); UiKit.AddTextColumn(tasks, "Title", "المهمة", 30); UiKit.AddTextColumn(tasks, "DeletedAt", "تاريخ الحذف", 25); }
        private void LoadData() { appointments.DataSource = new BindingList<Appointment>(database.GetDeletedAppointments()); tasks.DataSource = new BindingList<PatientTask>(database.GetDeletedTasks()); }
        private void Restore(int tab) { try { if (tab == 0) { Appointment a = appointments.CurrentRow == null ? null : appointments.CurrentRow.DataBoundItem as Appointment; if (a != null) database.RestoreAppointment(a.Id); } else { PatientTask t = tasks.CurrentRow == null ? null : tasks.CurrentRow.DataBoundItem as PatientTask; if (t != null) database.RestoreTask(t.Id); } LoadData(); } catch (Exception ex) { UiKit.ShowError(ex.Message); } }
    }
}
