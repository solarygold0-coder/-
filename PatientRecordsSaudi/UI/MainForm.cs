using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.UI
{
    public sealed class MainForm : Form
    {
        private readonly AppDatabase database; private readonly BackupService backups;
        private readonly TabControl tabs = new TabControl();
        private readonly DataGridView patientGrid = UiKit.Grid(), appointmentGrid = UiKit.Grid(), taskGrid = UiKit.Grid(), inventoryGrid = UiKit.Grid();
        private readonly DataGridView todayAppointments = UiKit.Grid(), dueTasks = UiKit.Grid();
        private readonly ComboBox searchMode = UiKit.Combo("الكل", "رقم الملف", "الهوية/الإقامة", "الاسم", "رقم الجوال"), sortMode = UiKit.Combo("رقم الملف", "الاسم", "الأحدث", "آخر مراجعة"), appointmentFilter = UiKit.Combo("القادمة", "اليوم", "هذا الأسبوع", "الكل");
        private readonly TextBox searchText = UiKit.TextBox(150), clinicName = UiKit.TextBox(150), clinicPhone = UiKit.TextBox(30), clinicAddress = UiKit.TextBox(250);
        private readonly CheckBox showArchived = new CheckBox { Text = "إظهار المؤرشفين", AutoSize = true, Font = UiKit.NormalFont, Margin = new Padding(10, 12, 10, 5) }, showCompleted = new CheckBox { Text = "إظهار المكتملة", AutoSize = true, Font = UiKit.NormalFont, Margin = new Padding(10, 12, 10, 5) };
        private readonly Label patientCount = DashboardNumber(), todayCount = DashboardNumber(), upcomingCount = DashboardNumber(), taskCount = DashboardNumber(), inventoryCount = DashboardNumber();
        private readonly NotifyIcon notify = new NotifyIcon(); private readonly Timer reminderTimer = new Timer();
        private readonly HashSet<Guid> notified = new HashSet<Guid>(); private Guid? notificationPatientId;

        public MainForm(AppDatabase database, BackupService backupService)
        {
            this.database = database; backups = backupService;
            AppSettings settings = database.GetSettings(); Text = "نظام إدارة سجلات المرضى - " + settings.ClinicName;
            RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; Font = UiKit.NormalFont; BackColor = UiKit.Background;
            StartPosition = FormStartPosition.CenterScreen; WindowState = FormWindowState.Maximized; MinimumSize = new Size(1024, 700); FormBorderStyle = FormBorderStyle.Sizable;
            BuildHeader(); BuildTabs(); ConfigureNotification(); LoadAll();
            Shown += delegate { AnnualInventoryAlert(); }; FormClosing += OnClosing;
        }

        private void BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = UiKit.Primary };
            var title = new Label { Text = "سجلات المرضى والمواعيد", Dock = DockStyle.Right, Width = 430, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.White, Font = new Font("Tahoma", 17, FontStyle.Bold), Padding = new Padding(0, 0, 20, 0) };
            DateTime now = DateTime.Now;
            var date = new Label { Text = SaudiValidation.ArabicDayName(now) + "، " + now.Day.ToString("00") + " - " + SaudiValidation.MonthLabel(now.Month) + " - " + now.Year.ToString("0000"), Dock = DockStyle.Left, Width = 360, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White, Font = UiKit.BoldFont, Padding = new Padding(20, 0, 0, 0) };
            header.Controls.Add(title); header.Controls.Add(date); Controls.Add(header);
        }

        private void BuildTabs()
        {
            tabs.Dock = DockStyle.Fill; tabs.Font = new Font("Tahoma", 11, FontStyle.Bold); tabs.Padding = new Point(18, 7);
            tabs.TabPages.Add(BuildDashboard()); tabs.TabPages.Add(BuildPatients()); tabs.TabPages.Add(BuildAppointments()); tabs.TabPages.Add(BuildTasks()); tabs.TabPages.Add(BuildInventory()); tabs.TabPages.Add(BuildSettings());
            Controls.Add(tabs);
        }

        private TabPage BuildDashboard()
        {
            var tab = NewTab("الملخص");
            var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 110, ColumnCount = 5, Padding = new Padding(10) };
            for (int i = 0; i < 5; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            cards.Controls.Add(Card("المراجعون النشطون", patientCount), 0, 0); cards.Controls.Add(Card("مواعيد اليوم", todayCount), 1, 0); cards.Controls.Add(Card("المواعيد القادمة", upcomingCount), 2, 0); cards.Controls.Add(Card("المهام المفتوحة", taskCount), 3, 0); cards.Controls.Add(Card("جرد 10 سنوات", inventoryCount), 4, 0);
            tab.Controls.Add(cards);
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 650, Padding = new Padding(10) };
            ConfigureAppointmentGrid(todayAppointments); ConfigureTaskGrid(dueTasks);
            split.Panel1.Controls.Add(WrapGrid("مواعيد اليوم — اضغط اسم المراجع لفتح ملفه", todayAppointments));
            split.Panel2.Controls.Add(WrapGrid("المهام القادمة — اضغط الاسم أو المهمة لفتح الملف", dueTasks)); tab.Controls.Add(split); split.BringToFront();
            WirePatientOpen(todayAppointments, "PatientId"); WirePatientOpen(dueTasks, "PatientId"); return tab;
        }

        private TabPage BuildPatients()
        {
            var tab = NewTab("المراجعون"); var tools = ToolPanel();
            tools.Controls.Add(UiKit.Button("مراجع جديد", NewPatient, false)); tools.Controls.Add(UiKit.Button("فتح/تعديل الملف", EditPatient, false)); tools.Controls.Add(UiKit.Button("أرشفة", ArchivePatient, true)); tools.Controls.Add(UiKit.Button("موعد جديد", NewAppointmentForSelected, false)); tools.Controls.Add(UiKit.Button("مهمة جديدة", NewTaskForSelected, false));
            tools.Controls.Add(showArchived); tools.Controls.Add(UiKit.Label("فرز:", true)); tools.Controls.Add(sortMode); tools.Controls.Add(UiKit.Label("بحث بـ:", true)); tools.Controls.Add(searchMode); searchText.Width = 230; searchText.Dock = DockStyle.None; tools.Controls.Add(searchText); tools.Controls.Add(UiKit.Button("بحث", delegate { LoadPatients(); }, false));
            tools.Controls.Add(new Label { Text = "تظهر أول 2000 نتيجة؛ استخدم البحث للوصول لأي ملف", AutoSize = true, ForeColor = Color.DimGray, Font = UiKit.NormalFont, Margin = new Padding(10, 12, 10, 5) });
            tab.Controls.Add(tools); ConfigurePatientGrid(); tab.Controls.Add(patientGrid); patientGrid.BringToFront();
            patientGrid.CellDoubleClick += delegate { OpenSelectedPatient(patientGrid); }; patientGrid.CellContentClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && patientGrid.Columns[e.ColumnIndex].Name == "FullName") OpenSelectedPatient(patientGrid); };
            searchText.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) LoadPatients(); }; showArchived.CheckedChanged += delegate { LoadPatients(); }; sortMode.SelectedIndexChanged += delegate { LoadPatients(); };
            return tab;
        }

        private TabPage BuildAppointments()
        {
            var tab = NewTab("المواعيد"); var tools = ToolPanel();
            tools.Controls.Add(UiKit.Button("موعد جديد", NewAppointment, false)); tools.Controls.Add(UiKit.Button("تعديل", EditAppointment, false)); tools.Controls.Add(UiKit.Button("طباعة الموعد", PrintAppointment, false)); tools.Controls.Add(UiKit.Button("حذف", DeleteAppointment, true)); tools.Controls.Add(UiKit.Label("عرض:", true)); tools.Controls.Add(appointmentFilter); tools.Controls.Add(UiKit.Button("تحديث", delegate { LoadAppointments(); }, false));
            tab.Controls.Add(tools); ConfigureAppointmentGrid(appointmentGrid); tab.Controls.Add(appointmentGrid); appointmentGrid.BringToFront(); appointmentFilter.SelectedIndexChanged += delegate { LoadAppointments(); }; WirePatientOpen(appointmentGrid, "PatientId"); return tab;
        }

        private TabPage BuildTasks()
        {
            var tab = NewTab("المهام والتنبيهات"); var tools = ToolPanel();
            tools.Controls.Add(UiKit.Button("مهمة جديدة", NewTask, false)); tools.Controls.Add(UiKit.Button("تعديل", EditTask, false)); tools.Controls.Add(UiKit.Button("تبديل مكتملة", ToggleTask, false)); tools.Controls.Add(UiKit.Button("حذف", DeleteTask, true)); tools.Controls.Add(showCompleted);
            tab.Controls.Add(tools); ConfigureTaskGrid(taskGrid); tab.Controls.Add(taskGrid); taskGrid.BringToFront(); showCompleted.CheckedChanged += delegate { LoadTasks(); }; WirePatientOpen(taskGrid, "PatientId"); return tab;
        }

        private TabPage BuildInventory()
        {
            var tab = NewTab("الجرد السنوي"); var top = ToolPanel();
            top.Controls.Add(UiKit.Button("فحص الآن", delegate { LoadInventory(); }, false)); top.Controls.Add(UiKit.Button("فتح ملف المراجع", delegate { OpenSelectedPatient(inventoryGrid); }, false)); top.Controls.Add(UiKit.Button("أرشفة المحدد بعد المراجعة", ArchiveInventoryPatient, true));
            top.Controls.Add(new Label { Text = "تظهر هنا الملفات التي مرّ على آخر مراجعة لها 10 سنوات. لا يتم الحذف تلقائيًا.", AutoSize = true, Font = UiKit.BoldFont, ForeColor = UiKit.Danger, Margin = new Padding(16, 12, 10, 5) });
            tab.Controls.Add(top); ConfigurePatientGrid(inventoryGrid); tab.Controls.Add(inventoryGrid); inventoryGrid.BringToFront(); inventoryGrid.CellDoubleClick += delegate { OpenSelectedPatient(inventoryGrid); }; inventoryGrid.CellContentClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && inventoryGrid.Columns[e.ColumnIndex].Name == "FullName") OpenSelectedPatient(inventoryGrid); }; return tab;
        }

        private TabPage BuildSettings()
        {
            var tab = NewTab("الإعدادات والنسخ الاحتياطي");
            var body = new TableLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(28), ColumnCount = 2, AutoSize = true };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
            AddSetting(body, "اسم المنشأة", clinicName); AddSetting(body, "هاتف المنشأة", clinicPhone); AddSetting(body, "عنوان المنشأة", clinicAddress);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            actions.Controls.Add(UiKit.Button("حفظ الإعدادات", SaveSettings, false)); actions.Controls.Add(UiKit.Button("إنشاء نسخة احتياطية", CreateBackup, false)); actions.Controls.Add(UiKit.Button("استعادة نسخة", RestoreBackup, true)); actions.Controls.Add(UiKit.Button("تصدير قائمة CSV", ExportCsv, false));
            int r = body.RowCount++; body.Controls.Add(new Label(), 0, r); body.Controls.Add(actions, 1, r);
            var privacy = new Label { Text = "تنبيه خصوصية: البيانات الصحية حساسة. قاعدة البيانات مشفرة، والنسخ الاحتياطية تحتوي بيانات مشفرة. امنع مشاركة كلمة المرور أو ملفات النسخ مع غير المخولين.", AutoSize = true, MaximumSize = new Size(850, 0), Font = UiKit.BoldFont, ForeColor = UiKit.Danger, Margin = new Padding(8, 24, 8, 8) };
            r = body.RowCount++; body.Controls.Add(new Label(), 0, r); body.Controls.Add(privacy, 1, r); tab.Controls.Add(body);
            AppSettings s = database.GetSettings(); clinicName.Text = s.ClinicName; clinicPhone.Text = s.ClinicPhone; clinicAddress.Text = s.ClinicAddress; return tab;
        }

        private static TabPage NewTab(string text) { return new TabPage(text) { BackColor = UiKit.Background, Padding = new Padding(6) }; }
        private static FlowLayoutPanel ToolPanel() { return new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoScroll = true, Padding = new Padding(6), BackColor = Color.White }; }
        private static Label DashboardNumber() { return new Label { Text = "0", AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiKit.Primary, Font = new Font("Tahoma", 20, FontStyle.Bold) }; }
        private static Control Card(string title, Label value)
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(6), BorderStyle = BorderStyle.FixedSingle };
            p.Controls.Add(value); p.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 34, TextAlign = ContentAlignment.MiddleCenter, Font = UiKit.BoldFont, ForeColor = Color.FromArgb(55, 65, 81) }); return p;
        }
        private static Control WrapGrid(string title, DataGridView grid)
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White }; p.Controls.Add(grid); p.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 42, TextAlign = ContentAlignment.MiddleRight, Font = UiKit.BoldFont, ForeColor = UiKit.Primary, Padding = new Padding(8) }); grid.BringToFront(); return p;
        }
        private static void AddSetting(TableLayoutPanel table, string label, Control control) { int r = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(UiKit.Label(label, true), 0, r); table.Controls.Add(control, 1, r); }

        private void ConfigurePatientGrid() { ConfigurePatientGrid(patientGrid); }
        private static void ConfigurePatientGrid(DataGridView g)
        {
            if (g.Columns.Count > 0) return; UiKit.AddTextColumn(g, "FileNumber", "رقم الملف", 14);
            g.Columns.Add(new DataGridViewLinkColumn { DataPropertyName = "FullName", Name = "FullName", HeaderText = "اسم المراجع", FillWeight = 30, LinkColor = UiKit.Primary, TrackVisitedState = false });
            UiKit.AddTextColumn(g, "NationalId", "الهوية/الإقامة", 20); UiKit.AddTextColumn(g, "Mobile", "الجوال", 18); UiKit.AddTextColumn(g, "City", "المدينة", 15); UiKit.AddTextColumn(g, "StatusText", "الحالة", 12); UiKit.AddTextColumn(g, "BirthDateText", "الميلاد", 16); g.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Id", Name = "Id", Visible = false });
        }
        private static void ConfigureAppointmentGrid(DataGridView g)
        {
            if (g.Columns.Count > 0) return; UiKit.AddTextColumn(g, "FileNumber", "رقم الملف", 12);
            g.Columns.Add(new DataGridViewLinkColumn { DataPropertyName = "PatientName", Name = "PatientName", HeaderText = "اسم المراجع", FillWeight = 26, LinkColor = UiKit.Primary, ActiveLinkColor = UiKit.Accent, TrackVisitedState = false });
            g.Columns.Add(new DataGridViewLinkColumn { DataPropertyName = "Title", Name = "Title", HeaderText = "الموعد", FillWeight = 24, LinkColor = UiKit.Primary, ActiveLinkColor = UiKit.Accent, TrackVisitedState = false });
            UiKit.AddTextColumn(g, "DateText", "التاريخ الميلادي", 28); UiKit.AddTextColumn(g, "TimeText", "الوقت", 14); UiKit.AddTextColumn(g, "VisitType", "النوع", 14); UiKit.AddTextColumn(g, "Status", "الحالة", 14); g.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Id", Name = "Id", Visible = false }); g.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "PatientId", Name = "PatientId", Visible = false });
        }
        private static void ConfigureTaskGrid(DataGridView g)
        {
            if (g.Columns.Count > 0) return; UiKit.AddTextColumn(g, "FileNumber", "رقم الملف", 12);
            g.Columns.Add(new DataGridViewLinkColumn { DataPropertyName = "PatientName", Name = "PatientName", HeaderText = "اسم المراجع", FillWeight = 28, LinkColor = UiKit.Primary, TrackVisitedState = false });
            g.Columns.Add(new DataGridViewLinkColumn { DataPropertyName = "Title", Name = "Title", HeaderText = "المهمة/التنبيه", FillWeight = 30, LinkColor = UiKit.Primary, TrackVisitedState = false });
            UiKit.AddTextColumn(g, "DueText", "الموعد الميلادي", 24); UiKit.AddTextColumn(g, "Priority", "الأولوية", 12); UiKit.AddTextColumn(g, "CompletionText", "الحالة", 12); g.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Id", Name = "Id", Visible = false }); g.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "PatientId", Name = "PatientId", Visible = false });
        }

        private void LoadAll() { LoadPatients(); LoadAppointments(); LoadTasks(); LoadInventory(); LoadDashboard(); }
        private void LoadPatients() { patientGrid.DataSource = new BindingList<Patient>(database.SearchPatients(searchMode.Text, searchText.Text, showArchived.Checked, sortMode.Text)); }
        private void LoadAppointments()
        {
            DateTime now = DateTime.Now, from = DateTime.MinValue, to = DateTime.MaxValue;
            if (appointmentFilter.Text == "القادمة") from = now;
            else if (appointmentFilter.Text == "اليوم") { from = DateTime.Today; to = from.AddDays(1); }
            else if (appointmentFilter.Text == "هذا الأسبوع") { from = DateTime.Today; to = from.AddDays(7); }
            appointmentGrid.DataSource = new BindingList<Appointment>(database.GetAppointments(from == DateTime.MinValue ? (DateTime?)null : from, to == DateTime.MaxValue ? (DateTime?)null : to));
        }
        private void LoadTasks() { taskGrid.DataSource = new BindingList<PatientTask>(database.GetTasks(showCompleted.Checked)); }
        private void LoadInventory() { var list = database.GetInventoryCandidates(DateTime.Today); inventoryGrid.DataSource = new BindingList<Patient>(list); inventoryCount.Text = list.Count.ToString("N0"); }
        private void LoadDashboard()
        {
            List<Appointment> today = database.GetAppointments(DateTime.Today, DateTime.Today.AddDays(1));
            List<Appointment> upcoming = database.GetAppointments(DateTime.Now, DateTime.Now.AddDays(30));
            List<PatientTask> tasks = database.GetTasks(false);
            patientCount.Text = database.CountActivePatients().ToString("N0"); todayCount.Text = today.Count.ToString("N0"); upcomingCount.Text = upcoming.Count.ToString("N0"); taskCount.Text = tasks.Count.ToString("N0");
            todayAppointments.DataSource = new BindingList<Appointment>(today); dueTasks.DataSource = new BindingList<PatientTask>(tasks.Take(30).ToList());
        }

        private Patient SelectedPatient(DataGridView grid)
        {
            if (grid.CurrentRow == null) return null; var p = grid.CurrentRow.DataBoundItem as Patient; if (p != null) return database.GetPatient(p.Id);
            Guid patientId; object v = grid.CurrentRow.Cells["PatientId"].Value; return v != null && Guid.TryParse(v.ToString(), out patientId) ? database.GetPatient(patientId) : null;
        }
        private Appointment SelectedAppointment() { return appointmentGrid.CurrentRow == null ? null : appointmentGrid.CurrentRow.DataBoundItem as Appointment; }
        private PatientTask SelectedTask() { return taskGrid.CurrentRow == null ? null : taskGrid.CurrentRow.DataBoundItem as PatientTask; }
        private void OpenSelectedPatient(DataGridView grid) { Patient p = SelectedPatient(grid); if (p == null) { UiKit.ShowError("اختر مراجعًا أولًا."); return; } OpenPatient(p); }
        private void OpenPatient(Patient patient)
        {
            using (var form = new PatientForm(patient)) if (form.ShowDialog(this) == DialogResult.OK) { try { database.UpdatePatient(form.Result); LoadAll(); } catch (Exception ex) { UiKit.ShowError(ex.Message); } }
        }
        private void WirePatientOpen(DataGridView grid, string idColumn)
        {
            grid.CellContentClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && (grid.Columns[e.ColumnIndex].Name == "PatientName" || grid.Columns[e.ColumnIndex].Name == "Title")) OpenSelectedPatient(grid); };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenSelectedPatient(grid); };
        }

        private void NewPatient(object sender, EventArgs e)
        {
            using (var f = new PatientForm(null)) if (f.ShowDialog(this) == DialogResult.OK)
            {
                try { Patient p = database.AddPatient(f.Result); LoadAll(); MessageBox.Show("تم إنشاء ملف المراجع بنجاح.\nرقم الملف: " + p.FileNumber, "تم الحفظ", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign); }
                catch (DuplicatePatientException ex) { if (UiKit.Confirm(ex.Message + "\nهل تريد فتح الملف الموجود؟", "احتمال تكرار")) OpenPatient(ex.ExistingPatient); }
                catch (Exception ex) { UiKit.ShowError(ex.Message); }
            }
        }
        private void EditPatient(object sender, EventArgs e) { OpenSelectedPatient(patientGrid); }
        private void ArchivePatient(object sender, EventArgs e)
        {
            Patient p = SelectedPatient(patientGrid); if (p == null) { UiKit.ShowError("اختر مراجعًا أولًا."); return; }
            if (p.IsArchived) { if (UiKit.Confirm("الملف مؤرشف. هل تريد استعادته؟", "استعادة الملف")) { database.RestorePatient(p.Id); LoadAll(); } return; }
            if (UiKit.Confirm("سيتم أرشفة الملف رقم " + p.FileNumber + " دون إعادة استخدام رقمه. ستبقى البيانات قابلة للاستعادة. هل تريد المتابعة؟", "تأكيد الأرشفة")) { database.ArchivePatient(p.Id, "أرشفة يدوية"); LoadAll(); }
        }
        private long? SelectedFileNumber() { Patient p = SelectedPatient(patientGrid); return p == null ? (long?)null : p.FileNumber; }
        private void NewAppointmentForSelected(object s, EventArgs e) { long? n = SelectedFileNumber(); if (!n.HasValue) { UiKit.ShowError("اختر مراجعًا أولًا."); return; } ShowAppointment(null, n); }
        private void NewTaskForSelected(object s, EventArgs e) { long? n = SelectedFileNumber(); if (!n.HasValue) { UiKit.ShowError("اختر مراجعًا أولًا."); return; } ShowTask(null, n); }
        private void NewAppointment(object s, EventArgs e) { ShowAppointment(null, null); }
        private void EditAppointment(object s, EventArgs e) { Appointment a = SelectedAppointment(); if (a == null) { UiKit.ShowError("اختر موعدًا أولًا."); return; } ShowAppointment(database.GetAppointment(a.Id), null); }
        private void ShowAppointment(Appointment a, long? number)
        {
            using (var f = new AppointmentForm(database, a, number)) if (f.ShowDialog(this) == DialogResult.OK) { try { if (a == null) database.AddAppointment(f.Result); else database.UpdateAppointment(f.Result); LoadAll(); } catch (Exception ex) { UiKit.ShowError(ex.Message); } }
        }
        private void DeleteAppointment(object s, EventArgs e) { Appointment a = SelectedAppointment(); if (a == null) return; if (UiKit.Confirm("حذف الموعد المحدد؟", "تأكيد الحذف")) { database.DeleteAppointment(a.Id); LoadAll(); } }
        private void NewTask(object s, EventArgs e) { ShowTask(null, null); }
        private void EditTask(object s, EventArgs e) { PatientTask t = SelectedTask(); if (t == null) { UiKit.ShowError("اختر مهمة أولًا."); return; } ShowTask(t, null); }
        private void ShowTask(PatientTask t, long? n) { using (var f = new TaskForm(database, t, n)) if (f.ShowDialog(this) == DialogResult.OK) { if (t == null) database.AddTask(f.Result); else database.UpdateTask(f.Result); LoadAll(); } }
        private void ToggleTask(object s, EventArgs e) { PatientTask t = SelectedTask(); if (t == null) return; t.IsCompleted = !t.IsCompleted; database.UpdateTask(t); LoadAll(); }
        private void DeleteTask(object s, EventArgs e) { PatientTask t = SelectedTask(); if (t != null && UiKit.Confirm("حذف المهمة المحددة؟", "تأكيد الحذف")) { database.DeleteTask(t.Id); LoadAll(); } }

        private void ArchiveInventoryPatient(object s, EventArgs e)
        {
            Patient p = SelectedPatient(inventoryGrid); if (p == null) { UiKit.ShowError("اختر مراجعًا من قائمة الجرد."); return; }
            if (UiKit.Confirm("تمضي 10 سنوات أو أكثر منذ آخر نشاط مسجل. هل راجعت الالتزامات النظامية وتريد أرشفة الملف رقم " + p.FileNumber + "؟", "أرشفة بعد الجرد")) { database.ArchivePatient(p.Id, "جرد سنوي: عدم مراجعة لمدة 10 سنوات"); LoadAll(); }
        }
        private void AnnualInventoryAlert()
        {
            AppSettings s = database.GetSettings(); if (s.LastInventoryAlertYear == DateTime.Today.Year) return; List<Patient> list = database.GetInventoryCandidates(DateTime.Today); database.SetInventoryAlerted(DateTime.Today.Year);
            if (list.Count > 0) { tabs.SelectedIndex = 4; MessageBox.Show("تنبيه الجرد السنوي: يوجد " + list.Count + " ملفًا لم يسجل له نشاط منذ 10 سنوات أو أكثر. راجع القائمة قبل الأرشفة.", "الجرد السنوي", MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign); }
        }

        private void SaveSettings(object s, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(clinicName.Text)) { UiKit.ShowError("اسم المنشأة مطلوب."); return; }
            AppSettings set = database.GetSettings(); set.ClinicName = clinicName.Text.Trim(); set.ClinicPhone = clinicPhone.Text.Trim(); set.ClinicAddress = clinicAddress.Text.Trim(); database.SaveSettings(set); Text = "نظام إدارة سجلات المرضى - " + set.ClinicName; MessageBox.Show("تم حفظ الإعدادات.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void CreateBackup(object s, EventArgs e)
        {
            using (var d = new FolderBrowserDialog { Description = "اختر مجلد حفظ النسخة الاحتياطية" }) if (d.ShowDialog(this) == DialogResult.OK) { try { string p = backups.CreateBackup(d.SelectedPath, database); MessageBox.Show("تم إنشاء النسخة الاحتياطية:\n" + p, "نجاح", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign); } catch (Exception ex) { UiKit.ShowError("تعذر إنشاء النسخة: " + ex.Message); } }
        }
        private void RestoreBackup(object s, EventArgs e)
        {
            if (!UiKit.Confirm("ستستبدل النسخة الاحتياطية كل البيانات الحالية، مع إنشاء نسخة أمان تلقائية. سيعاد تشغيل البرنامج. هل تريد المتابعة؟", "استعادة نسخة")) return;
            using (var o = new OpenFileDialog { Filter = "نسخة سجلات المرضى (*.zip)|*.zip", Title = "اختر النسخة الاحتياطية" }) if (o.ShowDialog(this) == DialogResult.OK) { try { backups.RestoreBackup(o.FileName, database); MessageBox.Show("تمت الاستعادة. سيعاد تشغيل البرنامج الآن.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information); Application.Restart(); } catch (Exception ex) { try { database.Reopen(); } catch { } UiKit.ShowError("تعذرت الاستعادة: " + ex.Message); } }
        }
        private void ExportCsv(object s, EventArgs e)
        {
            if (!UiKit.Confirm("سيحتوي ملف CSV على بيانات شخصية حساسة وغير مشفرة. احفظه في مكان آمن. هل تريد المتابعة؟", "تحذير خصوصية")) return;
            using (var save = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "قائمة_المراجعين_" + DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv" }) if (save.ShowDialog(this) == DialogResult.OK)
            {
                try { var sb = new StringBuilder(); sb.AppendLine("رقم الملف,الاسم,الهوية أو الإقامة,الجوال,المدينة,الحالة"); foreach (Patient p in database.GetAllPatients(true)) sb.AppendLine(Csv(p.FileNumber.ToString()) + "," + Csv(p.FullName) + "," + Csv(p.NationalId) + "," + Csv(p.Mobile) + "," + Csv(p.City) + "," + Csv(p.StatusText)); File.WriteAllText(save.FileName, sb.ToString(), new UTF8Encoding(true)); MessageBox.Show("تم التصدير.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                catch (Exception ex) { UiKit.ShowError(ex.Message); }
            }
        }
        private static string Csv(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }

        private void PrintAppointment(object s, EventArgs e)
        {
            Appointment a = SelectedAppointment(); if (a == null) { UiKit.ShowError("اختر موعدًا للطباعة."); return; } Patient p = database.GetPatient(a.PatientId); AppSettings set = database.GetSettings();
            var doc = new PrintDocument { DocumentName = "موعد_" + a.FileNumber }; doc.PrintPage += delegate(object sender, PrintPageEventArgs ev)
            {
                Rectangle r = ev.MarginBounds; var titleFont = new Font("Tahoma", 18, FontStyle.Bold); var headFont = new Font("Tahoma", 12, FontStyle.Bold); var textFont = new Font("Tahoma", 11);
                var center = new StringFormat { Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.DirectionRightToLeft }; var right = new StringFormat { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.DirectionRightToLeft };
                int y = r.Top; ev.Graphics.DrawString(set.ClinicName, titleFont, Brushes.Black, new RectangleF(r.Left, y, r.Width, 36), center); y += 48;
                ev.Graphics.DrawString("إشعار موعد", headFont, Brushes.Black, new RectangleF(r.Left, y, r.Width, 30), center); y += 55;
                string[] lines = { "رقم الملف: " + a.FileNumber, "اسم المراجع: " + a.PatientName, "رقم الهوية/الإقامة: " + (p == null ? "" : p.NationalId), "نوع الموعد: " + a.Title + " - " + a.VisitType, "التاريخ الميلادي: " + a.DateText + " (" + SaudiValidation.ArabicDayName(a.StartsAt) + ")", "الوقت: " + a.TimeText, "مدة الموعد: " + a.DurationMinutes + " دقيقة", "الحالة: " + a.Status };
                foreach (string line in lines) { ev.Graphics.DrawString(line, textFont, Brushes.Black, new RectangleF(r.Left, y, r.Width, 28), right); y += 34; }
                y += 25; ev.Graphics.DrawString("يرجى الحضور قبل الموعد بـ 15 دقيقة.", headFont, Brushes.Black, new RectangleF(r.Left, y, r.Width, 30), center); y += 55;
                ev.Graphics.DrawString(set.ClinicAddress + "   " + set.ClinicPhone, textFont, Brushes.Black, new RectangleF(r.Left, y, r.Width, 30), center);
                titleFont.Dispose(); headFont.Dispose(); textFont.Dispose(); center.Dispose(); right.Dispose();
            };
            using (var preview = new PrintPreviewDialog { Document = doc, Width = 1000, Height = 750, RightToLeft = RightToLeft.Yes }) preview.ShowDialog(this);
        }

        private void ConfigureNotification()
        {
            notify.Icon = SystemIcons.Information; notify.Visible = true; notify.Text = "سجلات المرضى"; notify.BalloonTipClicked += delegate { if (notificationPatientId.HasValue) { Patient p = database.GetPatient(notificationPatientId.Value); if (p != null) { Show(); WindowState = FormWindowState.Maximized; Activate(); OpenPatient(p); } } };
            reminderTimer.Interval = 60000; reminderTimer.Tick += delegate { CheckReminders(); }; reminderTimer.Start();
        }
        private void CheckReminders()
        {
            DateTime now = DateTime.Now, soon = now.AddMinutes(5);
            Appointment a = database.GetAppointments(now.AddMinutes(-1), soon).FirstOrDefault(x => !notified.Contains(x.Id) && x.Status != "ملغي");
            if (a != null) { notified.Add(a.Id); notificationPatientId = a.PatientId; notify.BalloonTipTitle = "موعد قريب"; notify.BalloonTipText = a.PatientName + " — " + a.Title + " — " + a.TimeText; notify.ShowBalloonTip(10000); return; }
            PatientTask t = database.GetTasks(false).FirstOrDefault(x => x.DueAt >= now.AddMinutes(-1) && x.DueAt <= soon && !notified.Contains(x.Id));
            if (t != null) { notified.Add(t.Id); notificationPatientId = t.PatientId; notify.BalloonTipTitle = "تنبيه مهمة"; notify.BalloonTipText = t.PatientName + " — " + t.Title; notify.ShowBalloonTip(10000); }
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            reminderTimer.Stop(); notify.Visible = false; try { string auto = Path.Combine(database.DataDirectory, "AutoBackups"); Directory.CreateDirectory(auto); if (!Directory.GetFiles(auto, "نسخة_سجلات_المرضى_" + DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "*.zip").Any()) backups.CreateBackup(auto, database); foreach (FileInfo f in new DirectoryInfo(auto).GetFiles("*.zip").OrderByDescending(x => x.CreationTime).Skip(30)) f.Delete(); } catch { }
        }
    }
}
