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
using Microsoft.Win32;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.UI
{
    public sealed class MainForm : Form
    {
        private readonly AppDatabase database; private readonly BackupService backups; private readonly AppSecurity security; private SecuritySession session;
        private readonly TabControl tabs = new TabControl();
        private readonly DataGridView patientGrid = UiKit.Grid(), appointmentGrid = UiKit.Grid(), taskGrid = UiKit.Grid(), inventoryGrid = UiKit.Grid();
        private readonly DataGridView todayAppointments = UiKit.Grid(), dueTasks = UiKit.Grid();
        private readonly ComboBox searchMode = UiKit.Combo("الكل", "رقم الملف", "الهوية/الإقامة", "الاسم", "رقم الجوال", "المدينة"), sortMode = UiKit.Combo("رقم الملف", "الاسم", "الأحدث", "آخر مراجعة"), appointmentFilter = UiKit.Combo("القادمة", "اليوم", "هذا الأسبوع", "الكل");
        private readonly TextBox searchText = UiKit.TextBox(150), clinicName = UiKit.TextBox(150), clinicPhone = UiKit.TextBox(30), clinicAddress = UiKit.TextBox(250), autoBackupDirectory = UiKit.TextBox(500);
        private readonly TextBox visitTypesText = UiKit.TextBox(1000), appointmentStatusesText = UiKit.TextBox(1000), taskPrioritiesText = UiKit.TextBox(500), genderOptionsText = UiKit.TextBox(500), bloodTypesText = UiKit.TextBox(500);
        private readonly ComboBox workStart = UiKit.Combo("06:00", "07:00", "08:00", "09:00", "10:00"), workEnd = UiKit.Combo("14:00", "15:00", "16:00", "17:00", "18:00", "19:00", "20:00"), backupHours = UiKit.Combo("1", "2", "4", "6", "8", "12", "24");
        private readonly Label backupStatus = new Label { AutoSize = true, Font = UiKit.BoldFont, ForeColor = Color.DimGray, Margin = new Padding(8, 12, 8, 8) };
        private readonly Label clinicLogoStatus = new Label { AutoSize = true, Font = UiKit.NormalFont, ForeColor = Color.DimGray, Margin = new Padding(8, 12, 8, 8) };
        private readonly CheckBox showArchived = new CheckBox { Text = "إظهار المؤرشفين", AutoSize = true, Font = UiKit.NormalFont, Margin = new Padding(10, 12, 10, 5) }, showCompleted = new CheckBox { Text = "إظهار المكتملة", AutoSize = true, Font = UiKit.NormalFont, Margin = new Padding(10, 12, 10, 5) };
        private readonly Label patientCount = DashboardNumber(), todayCount = DashboardNumber(), upcomingCount = DashboardNumber(), taskCount = DashboardNumber(), inventoryCount = DashboardNumber();
        private readonly NotifyIcon notify = new NotifyIcon(); private readonly Timer reminderTimer = new Timer(), maintenanceTimer = new Timer(), idleTimer = new Timer();
        private Guid? notificationPatientId; private bool forceExit, locked; private readonly InactivityFilter activity = new InactivityFilter();

        public MainForm(AppDatabase database, BackupService backupService, AppSecurity security, SecuritySession session)
        {
            this.database = database; backups = backupService; this.security = security; this.session = session;
            AppSettings settings = database.GetSettings(); Text = "نظام إدارة سجلات المراجعين - " + settings.ClinicName;
            RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; Font = UiKit.NormalFont; BackColor = UiKit.Background;
            StartPosition = FormStartPosition.CenterScreen; WindowState = FormWindowState.Maximized; MinimumSize = new Size(1024, 700); FormBorderStyle = FormBorderStyle.Sizable;
            BuildHeader(); BuildTabs(); ConfigureNotification(); LoadAll();
            Application.AddMessageFilter(activity); idleTimer.Interval = 30000; idleTimer.Tick += delegate { if (!locked && DateTime.Now - activity.LastActivity > TimeSpan.FromMinutes(15)) LockApplication(); }; idleTimer.Start();
            maintenanceTimer.Interval = 15 * 60 * 1000; maintenanceTimer.Tick += delegate { RunScheduledBackup(false); }; maintenanceTimer.Start();
            Shown += delegate { AnnualInventoryAlert(); RunScheduledBackup(false); }; FormClosing += OnClosing;
        }

        private void BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = UiKit.Primary };
            var title = new Label { Text = "سجلات المراجعين والمواعيد", Dock = DockStyle.Right, Width = 430, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.White, Font = new Font("Tahoma", 17, FontStyle.Bold), Padding = new Padding(0, 0, 20, 0) };
            DateTime now = DateTime.Now;
            var date = new Label { Text = SaudiValidation.ArabicDayName(now) + "، " + now.Day.ToString("00") + " - " + SaudiValidation.MonthLabel(now.Month) + " - " + now.Year.ToString("0000"), Dock = DockStyle.Left, Width = 360, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White, Font = UiKit.BoldFont, Padding = new Padding(20, 0, 0, 0) };
            var user = new Label { Text = session.DisplayName + " — " + session.Role, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = UiKit.BoldFont };
            header.Controls.Add(user); header.Controls.Add(title); header.Controls.Add(date); Controls.Add(header);
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
            Button add = UiKit.Button("مراجع جديد", NewPatient, false), edit = UiKit.Button(session.IsReadOnly ? "فتح الملف" : "فتح/تعديل الملف", EditPatient, false), archive = UiKit.Button("أرشفة", ArchivePatient, true), appointment = UiKit.Button("موعد جديد", NewAppointmentForSelected, false), task = UiKit.Button("مهمة جديدة", NewTaskForSelected, false);
            add.Enabled = appointment.Enabled = task.Enabled = !session.IsReadOnly; archive.Enabled = session.IsAdmin; tools.Controls.Add(add); tools.Controls.Add(edit); tools.Controls.Add(archive); tools.Controls.Add(appointment); tools.Controls.Add(task); tools.Controls.Add(UiKit.Button("طباعة ملخص المراجع", PrintPatientSummary, false));
            tools.Controls.Add(showArchived); tools.Controls.Add(UiKit.Label("فرز:", true)); tools.Controls.Add(sortMode); tools.Controls.Add(UiKit.Label("بحث بـ:", true)); tools.Controls.Add(searchMode); searchText.Width = 230; searchText.Dock = DockStyle.None; tools.Controls.Add(searchText); tools.Controls.Add(UiKit.Button("بحث", delegate { LoadPatients(); }, false));
            tools.Controls.Add(new Label { Text = "السعة 10,000 مراجع؛ البحث بالملف أو الهوية أو الاسم أو الجوال أو المدينة", AutoSize = true, ForeColor = Color.DimGray, Font = UiKit.NormalFont, Margin = new Padding(10, 12, 10, 5) });
            tab.Controls.Add(tools); ConfigurePatientGrid(); tab.Controls.Add(patientGrid); patientGrid.BringToFront();
            patientGrid.CellDoubleClick += delegate { OpenSelectedPatient(patientGrid); }; patientGrid.CellContentClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && patientGrid.Columns[e.ColumnIndex].Name == "FullName") OpenSelectedPatient(patientGrid); };
            searchText.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) LoadPatients(); }; showArchived.CheckedChanged += delegate { LoadPatients(); }; sortMode.SelectedIndexChanged += delegate { LoadPatients(); };
            return tab;
        }

        private TabPage BuildAppointments()
        {
            var tab = NewTab("المواعيد"); var tools = ToolPanel();
            Button add = UiKit.Button("موعد جديد", NewAppointment, false), edit = UiKit.Button("تعديل", EditAppointment, false), delete = UiKit.Button("نقل للمحذوفات", DeleteAppointment, true); add.Enabled = edit.Enabled = !session.IsReadOnly; delete.Enabled = session.IsAdmin;
            Button export = UiKit.Button("تصدير CSV", ExportAppointmentsCsv, false); export.Enabled = session.IsAdmin;
            tools.Controls.Add(add); tools.Controls.Add(edit); tools.Controls.Add(UiKit.Button("طباعة الموعد", PrintAppointment, false)); tools.Controls.Add(export); tools.Controls.Add(delete); tools.Controls.Add(UiKit.Label("عرض:", true)); tools.Controls.Add(appointmentFilter); tools.Controls.Add(UiKit.Button("تحديث", delegate { LoadAppointments(); }, false));
            tab.Controls.Add(tools); ConfigureAppointmentGrid(appointmentGrid); tab.Controls.Add(appointmentGrid); appointmentGrid.BringToFront(); appointmentFilter.SelectedIndexChanged += delegate { LoadAppointments(); }; WirePatientOpen(appointmentGrid, "PatientId"); return tab;
        }

        private TabPage BuildTasks()
        {
            var tab = NewTab("المهام والتنبيهات"); var tools = ToolPanel();
            Button add = UiKit.Button("مهمة جديدة", NewTask, false), edit = UiKit.Button("تعديل", EditTask, false), toggle = UiKit.Button("تبديل مكتملة", ToggleTask, false), delete = UiKit.Button("نقل للمحذوفات", DeleteTask, true); add.Enabled = edit.Enabled = toggle.Enabled = !session.IsReadOnly; delete.Enabled = session.IsAdmin;
            tools.Controls.Add(add); tools.Controls.Add(edit); tools.Controls.Add(toggle); tools.Controls.Add(delete); tools.Controls.Add(showCompleted);
            tab.Controls.Add(tools); ConfigureTaskGrid(taskGrid); tab.Controls.Add(taskGrid); taskGrid.BringToFront(); showCompleted.CheckedChanged += delegate { LoadTasks(); }; WirePatientOpen(taskGrid, "PatientId"); return tab;
        }

        private TabPage BuildInventory()
        {
            var tab = NewTab("الجرد السنوي"); var top = ToolPanel();
            top.Controls.Add(UiKit.Button("فحص الآن", delegate { LoadInventory(); }, false)); top.Controls.Add(UiKit.Button("فتح ملف المراجع", delegate { OpenSelectedPatient(inventoryGrid); }, false)); Button archive = UiKit.Button("أرشفة المحدد بعد المراجعة", ArchiveInventoryPatient, true); archive.Enabled = session.IsAdmin; top.Controls.Add(archive);
            top.Controls.Add(new Label { Text = "تظهر هنا الملفات التي مرّ على آخر مراجعة لها 10 سنوات. لا يتم الحذف تلقائيًا.", AutoSize = true, Font = UiKit.BoldFont, ForeColor = UiKit.Danger, Margin = new Padding(16, 12, 10, 5) });
            tab.Controls.Add(top); ConfigurePatientGrid(inventoryGrid); tab.Controls.Add(inventoryGrid); inventoryGrid.BringToFront(); inventoryGrid.CellDoubleClick += delegate { OpenSelectedPatient(inventoryGrid); }; inventoryGrid.CellContentClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && inventoryGrid.Columns[e.ColumnIndex].Name == "FullName") OpenSelectedPatient(inventoryGrid); }; return tab;
        }

        private TabPage BuildSettings()
        {
            var tab = NewTab("الإعدادات والنسخ الاحتياطي"); tab.AutoScroll = true;
            var body = new TableLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(28), ColumnCount = 2, AutoSize = true };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
            AddSetting(body, "اسم المنشأة", clinicName); AddSetting(body, "هاتف المنشأة", clinicPhone); AddSetting(body, "عنوان المنشأة", clinicAddress); AddSetting(body, "بداية الدوام", workStart); AddSetting(body, "نهاية الدوام", workEnd); AddSetting(body, "نسخة تلقائية كل (ساعة)", backupHours);
            var backupLocation = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft }; autoBackupDirectory.Width = 500; autoBackupDirectory.ReadOnly = true; Button chooseBackupDirectory = UiKit.Button("اختيار مجلد خارجي", ChooseAutoBackupDirectory, false); chooseBackupDirectory.Enabled = session.IsAdmin; backupLocation.Controls.Add(chooseBackupDirectory); backupLocation.Controls.Add(autoBackupDirectory); AddSetting(body, "مجلد النسخ التلقائي", backupLocation);
            var logoActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft }; Button chooseLogo = UiKit.Button("اختيار شعار", ChooseClinicLogo, false), removeLogo = UiKit.Button("إزالة الشعار", RemoveClinicLogo, true); chooseLogo.Enabled = removeLogo.Enabled = session.IsAdmin; logoActions.Controls.Add(chooseLogo); logoActions.Controls.Add(removeLogo); logoActions.Controls.Add(clinicLogoStatus); AddSetting(body, "شعار الطباعة", logoActions);
            ConfigureLookupText(visitTypesText); ConfigureLookupText(appointmentStatusesText); ConfigureLookupText(taskPrioritiesText); ConfigureLookupText(genderOptionsText); ConfigureLookupText(bloodTypesText);
            AddSetting(body, "أنواع الزيارة (سطر لكل قيمة)", visitTypesText); AddSetting(body, "حالات الموعد", appointmentStatusesText); AddSetting(body, "أولويات المهام", taskPrioritiesText); AddSetting(body, "خيارات الجنس", genderOptionsText); AddSetting(body, "فصائل الدم", bloodTypesText);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            Button save = UiKit.Button("حفظ الإعدادات", SaveSettings, false), restore = UiKit.Button("استعادة نسخة", RestoreBackup, true), export = UiKit.Button("تصدير قائمة CSV", ExportCsv, false), users = UiKit.Button("حسابات الموظفين", ManageUsers, false), closures = UiKit.Button("الإجازات وأيام الإغلاق", ManageClosures, false), recycle = UiKit.Button("المحذوفات", OpenRecycleBin, false);
            Button audit = UiKit.Button("سجل العمليات", ShowAudit, false), exportAudit = UiKit.Button("تصدير السجل CSV", ExportAuditCsv, false), createBackup = UiKit.Button("إنشاء نسخة احتياطية", CreateBackup, false), purgeAttachments = UiKit.Button("إتلاف المرفقات المحذوفة القديمة", PurgeDeletedAttachments, true); save.Enabled = restore.Enabled = export.Enabled = users.Enabled = closures.Enabled = recycle.Enabled = audit.Enabled = exportAudit.Enabled = createBackup.Enabled = purgeAttachments.Enabled = session.IsAdmin;
            actions.Controls.Add(save); actions.Controls.Add(createBackup); actions.Controls.Add(restore); actions.Controls.Add(export); actions.Controls.Add(users); actions.Controls.Add(closures); actions.Controls.Add(recycle); actions.Controls.Add(UiKit.Button("تغيير كلمة المرور", ChangePassword, false)); actions.Controls.Add(audit); actions.Controls.Add(exportAudit); actions.Controls.Add(purgeAttachments);
            int r = body.RowCount++; body.Controls.Add(new Label(), 0, r); body.Controls.Add(actions, 1, r);
            var privacy = new Label { Text = "تنبيه خصوصية: البيانات الصحية حساسة. قاعدة البيانات مشفرة، والنسخ الاحتياطية تحتوي بيانات مشفرة. امنع مشاركة كلمة المرور أو ملفات النسخ مع غير المخولين.", AutoSize = true, MaximumSize = new Size(850, 0), Font = UiKit.BoldFont, ForeColor = UiKit.Danger, Margin = new Padding(8, 24, 8, 8) };
            r = body.RowCount++; body.Controls.Add(new Label(), 0, r); body.Controls.Add(privacy, 1, r); r = body.RowCount++; body.Controls.Add(new Label(), 0, r); body.Controls.Add(backupStatus, 1, r); tab.Controls.Add(body);
            AppSettings s = database.GetSettings(); clinicName.Text = s.ClinicName; clinicPhone.Text = s.ClinicPhone; clinicAddress.Text = s.ClinicAddress; workStart.SelectedItem = MinutesText(s.WorkDayStartMinutes); workEnd.SelectedItem = MinutesText(s.WorkDayEndMinutes); backupHours.SelectedItem = s.BackupIntervalHours.ToString(); autoBackupDirectory.Text = string.IsNullOrWhiteSpace(s.AutoBackupDirectory) ? "داخل الجهاز (اختر قرصًا خارجيًا للحماية من تعطل القرص)" : s.AutoBackupDirectory;
            visitTypesText.Lines = s.VisitTypes.ToArray(); appointmentStatusesText.Lines = s.AppointmentStatuses.ToArray(); taskPrioritiesText.Lines = s.TaskPriorities.ToArray(); genderOptionsText.Lines = s.GenderOptions.ToArray(); bloodTypesText.Lines = s.BloodTypes.ToArray(); clinicLogoStatus.Text = string.IsNullOrWhiteSpace(s.ClinicLogoStoredId) ? "لا يوجد شعار" : "الشعار الحالي: " + s.ClinicLogoFileName; backupStatus.Text = "حالة النسخ: " + s.LastBackupStatus;
            if (!session.IsAdmin) foreach (Control c in new Control[] { clinicName, clinicPhone, clinicAddress, workStart, workEnd, backupHours, visitTypesText, appointmentStatusesText, taskPrioritiesText, genderOptionsText, bloodTypesText }) c.Enabled = false; return tab;
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
        private static void ConfigureLookupText(TextBox box) { box.Multiline = true; box.Height = 64; box.ScrollBars = ScrollBars.Vertical; box.Dock = DockStyle.Top; }
        private static string MinutesText(int minutes) { return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00"); }
        private static int ParseMinutes(string value) { TimeSpan t; if (!TimeSpan.TryParse(value, out t)) throw new InvalidOperationException("وقت الدوام غير صحيح."); return (int)t.TotalMinutes; }

        private void ConfigurePatientGrid() { ConfigurePatientGrid(patientGrid); }
        private static void ConfigurePatientGrid(DataGridView g)
        {
            if (g.Columns.Count > 0) return; UiKit.AddTextColumn(g, "FileNumber", "رقم الملف", 14);
            g.Columns.Add(new DataGridViewLinkColumn { DataPropertyName = "FullName", Name = "FullName", HeaderText = "اسم المراجع", FillWeight = 30, LinkColor = UiKit.Primary, TrackVisitedState = false });
            UiKit.AddTextColumn(g, "NationalId", "الهوية/الإقامة", 20); UiKit.AddTextColumn(g, "Mobile", "الجوال", 18); UiKit.AddTextColumn(g, "City", "المدينة", 15); UiKit.AddTextColumn(g, "StatusText", "الحالة", 12); UiKit.AddTextColumn(g, "BirthDateText", "الميلاد", 16); g.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Id", Name = "Id", Visible = false });
            g.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e) { if (e.RowIndex >= 0 && g.Rows[e.RowIndex].DataBoundItem is Patient && ((Patient)g.Rows[e.RowIndex].DataBoundItem).IsArchived) { e.CellStyle.ForeColor = Color.FromArgb(107, 114, 128); e.CellStyle.BackColor = Color.FromArgb(243, 244, 246); } };
        }
        private static void ConfigureAppointmentGrid(DataGridView g)
        {
            if (g.Columns.Count > 0) return; UiKit.AddTextColumn(g, "FileNumber", "رقم الملف", 12);
            g.Columns.Add(new DataGridViewLinkColumn { DataPropertyName = "PatientName", Name = "PatientName", HeaderText = "اسم المراجع", FillWeight = 26, LinkColor = UiKit.Primary, ActiveLinkColor = UiKit.Accent, TrackVisitedState = false });
            g.Columns.Add(new DataGridViewLinkColumn { DataPropertyName = "Title", Name = "Title", HeaderText = "الموعد", FillWeight = 24, LinkColor = UiKit.Primary, ActiveLinkColor = UiKit.Accent, TrackVisitedState = false });
            UiKit.AddTextColumn(g, "DateText", "التاريخ الميلادي", 28); UiKit.AddTextColumn(g, "TimeText", "الوقت", 14); UiKit.AddTextColumn(g, "VisitType", "النوع", 14); UiKit.AddTextColumn(g, "Status", "الحالة", 14); g.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Id", Name = "Id", Visible = false }); g.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "PatientId", Name = "PatientId", Visible = false });
            g.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e) { if (e.RowIndex < 0) return; Appointment a = g.Rows[e.RowIndex].DataBoundItem as Appointment; if (a == null) return; if (a.Status == "ملغي") e.CellStyle.ForeColor = UiKit.Danger; else if (a.Status == "حضر") e.CellStyle.ForeColor = Color.FromArgb(21, 128, 61); else if (a.Status == "لم يحضر" || a.Status == "بانتظار التأكيد") e.CellStyle.ForeColor = Color.FromArgb(161, 98, 7); };
        }
        private static void ConfigureTaskGrid(DataGridView g)
        {
            if (g.Columns.Count > 0) return; UiKit.AddTextColumn(g, "FileNumber", "رقم الملف", 12);
            g.Columns.Add(new DataGridViewLinkColumn { DataPropertyName = "PatientName", Name = "PatientName", HeaderText = "اسم المراجع", FillWeight = 28, LinkColor = UiKit.Primary, TrackVisitedState = false });
            g.Columns.Add(new DataGridViewLinkColumn { DataPropertyName = "Title", Name = "Title", HeaderText = "المهمة/التنبيه", FillWeight = 30, LinkColor = UiKit.Primary, TrackVisitedState = false });
            UiKit.AddTextColumn(g, "DueText", "الموعد الميلادي", 24); UiKit.AddTextColumn(g, "Priority", "الأولوية", 12); UiKit.AddTextColumn(g, "CompletionText", "الحالة", 12); g.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Id", Name = "Id", Visible = false }); g.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "PatientId", Name = "PatientId", Visible = false });
            g.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e) { if (e.RowIndex < 0) return; PatientTask t = g.Rows[e.RowIndex].DataBoundItem as PatientTask; if (t == null) return; if (t.IsCompleted) e.CellStyle.ForeColor = Color.FromArgb(21, 128, 61); else if (t.Priority == "عاجلة") e.CellStyle.ForeColor = UiKit.Danger; else if (t.Priority == "عالية" || t.Priority == "مرتفعة") e.CellStyle.ForeColor = Color.FromArgb(161, 98, 7); };
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
            bool viewOnly = session.IsReadOnly || patient.IsArchived;
            using (var form = new PatientForm(database, patient, viewOnly)) if (form.ShowDialog(this) == DialogResult.OK) { try { database.UpdatePatient(form.Result); LoadAll(); } catch (Exception ex) { UiKit.ShowError(ex.Message); } }
        }
        private void WirePatientOpen(DataGridView grid, string idColumn)
        {
            grid.CellContentClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && (grid.Columns[e.ColumnIndex].Name == "PatientName" || grid.Columns[e.ColumnIndex].Name == "Title")) OpenSelectedPatient(grid); };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenSelectedPatient(grid); };
        }

        private void NewPatient(object sender, EventArgs e)
        {
            using (var f = new PatientForm(database, null, false)) if (f.ShowDialog(this) == DialogResult.OK)
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
            if (UiKit.Confirm("سيتم أرشفة الملف رقم " + p.FileNumber + " دون إعادة استخدام رقمه، وإلغاء مواعيده المستقبلية وإغلاق مهامه المفتوحة. ستبقى البيانات قابلة للاستعادة. هل تريد المتابعة؟", "تأكيد الأرشفة")) { database.ArchivePatient(p.Id, "أرشفة يدوية", true); LoadAll(); }
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
        private void DeleteAppointment(object s, EventArgs e) { Appointment a = SelectedAppointment(); if (a == null) return; if (UiKit.Confirm("نقل الموعد إلى المحذوفات مع إمكانية استعادته؟", "تأكيد")) { database.DeleteAppointment(a.Id); LoadAll(); } }
        private void NewTask(object s, EventArgs e) { ShowTask(null, null); }
        private void EditTask(object s, EventArgs e) { PatientTask t = SelectedTask(); if (t == null) { UiKit.ShowError("اختر مهمة أولًا."); return; } ShowTask(t, null); }
        private void ShowTask(PatientTask t, long? n) { using (var f = new TaskForm(database, t, n)) if (f.ShowDialog(this) == DialogResult.OK) { try { if (t == null) database.AddTask(f.Result); else database.UpdateTask(f.Result); LoadAll(); } catch (Exception ex) { UiKit.ShowError(ex.Message); } } }
        private void ToggleTask(object s, EventArgs e) { PatientTask t = SelectedTask(); if (t == null) return; try { t.IsCompleted = !t.IsCompleted; database.UpdateTask(t); LoadAll(); } catch (Exception ex) { UiKit.ShowError(ex.Message); } }
        private void DeleteTask(object s, EventArgs e) { PatientTask t = SelectedTask(); if (t != null && UiKit.Confirm("نقل المهمة إلى المحذوفات مع إمكانية استعادتها؟", "تأكيد")) { try { database.DeleteTask(t.Id); LoadAll(); } catch (Exception ex) { UiKit.ShowError(ex.Message); } } }

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
            try
            {
                AppSettings set = database.GetSettings(); set.ClinicName = clinicName.Text.Trim(); set.ClinicPhone = clinicPhone.Text.Trim(); set.ClinicAddress = clinicAddress.Text.Trim(); set.WorkDayStartMinutes = ParseMinutes(workStart.Text); set.WorkDayEndMinutes = ParseMinutes(workEnd.Text); int hours; if (!int.TryParse(backupHours.Text, out hours)) hours = 4; set.BackupIntervalHours = hours; set.AutoBackupDirectory = autoBackupDirectory.Text.StartsWith("داخل الجهاز", StringComparison.Ordinal) ? "" : autoBackupDirectory.Text.Trim();
                set.VisitTypes = LookupLines(visitTypesText); set.AppointmentStatuses = LookupLines(appointmentStatusesText); set.TaskPriorities = LookupLines(taskPrioritiesText); set.GenderOptions = LookupLines(genderOptionsText); set.BloodTypes = LookupLines(bloodTypesText);
                database.SaveSettings(set); Text = "نظام إدارة سجلات المراجعين - " + set.ClinicName; MessageBox.Show("تم حفظ الإعدادات.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { UiKit.ShowError(ex.Message); }
        }
        private static List<string> LookupLines(TextBox box) { return box.Lines.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).ToList(); }
        private void ChooseAutoBackupDirectory(object sender, EventArgs e)
        {
            using (var folder = new FolderBrowserDialog { Description = "اختر مجلدًا على قرص خارجي أو موقع نسخ آمن" }) if (folder.ShowDialog(this) == DialogResult.OK) autoBackupDirectory.Text = folder.SelectedPath;
        }
        private void ChooseClinicLogo(object sender, EventArgs e)
        {
            using (var open = new OpenFileDialog { Filter = "صور الشعار|*.png;*.jpg;*.jpeg", Title = "اختر شعار المنشأة" }) if (open.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    using (Image image = Image.FromFile(open.FileName)) { if (image.Width < 32 || image.Height < 32) throw new InvalidOperationException("أبعاد الشعار صغيرة جدًا."); }
                    database.SetClinicLogo(open.FileName); clinicLogoStatus.Text = "الشعار الحالي: " + Path.GetFileName(open.FileName);
                }
                catch (Exception ex) { UiKit.ShowError("تعذر حفظ الشعار: " + ex.Message); }
            }
        }
        private void RemoveClinicLogo(object sender, EventArgs e)
        {
            if (!UiKit.Confirm("إزالة شعار المنشأة من الطباعة؟", "إزالة الشعار")) return; database.RemoveClinicLogo(); clinicLogoStatus.Text = "لا يوجد شعار";
        }
        private void CreateBackup(object s, EventArgs e)
        {
            if (!session.IsAdmin) { UiKit.ShowError("إنشاء نسخة احتياطية يدوية متاح للمدير فقط."); return; }
            using (var d = new FolderBrowserDialog { Description = "اختر مجلد حفظ النسخة الاحتياطية" }) if (d.ShowDialog(this) == DialogResult.OK) { try { string p = backups.CreateBackup(d.SelectedPath, database); UpdateBackupStatus("نجحت في " + DateTime.Now.ToString("yyyy/MM/dd HH:mm"), DateTime.Now); MessageBox.Show("تم إنشاء النسخة الاحتياطية وفحصها:\n" + p, "نجاح", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign); } catch (Exception ex) { UpdateBackupStatus("فشلت: " + ex.Message, null); UiKit.ShowError("تعذر إنشاء النسخة: " + ex.Message); } }
        }
        private void PurgeDeletedAttachments(object sender, EventArgs e)
        {
            if (!UiKit.Confirm("سيتم الإتلاف النهائي للمرفقات التي حُذفت منذ أكثر من 90 يومًا، ولن يمكن استعادتها. تأكد من سياسة الاحتفاظ في المنشأة. هل تريد المتابعة؟", "إتلاف نهائي")) return;
            if (!UiKit.Confirm("تأكيد أخير: هذه العملية غير قابلة للتراجع.", "تأكيد الإتلاف")) return;
            try { int count = database.PurgeDeletedAttachments(DateTime.Now.AddDays(-90)); MessageBox.Show("تم إتلاف " + count + " مرفقًا محذوفًا قديمًا.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            catch (Exception ex) { UiKit.ShowError(ex.Message); }
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
                try { var sb = new StringBuilder(); sb.AppendLine("رقم الملف,الاسم,الهوية أو الإقامة,الجوال,المدينة,الحالة"); foreach (Patient p in database.GetAllPatients(true)) sb.AppendLine(Csv(p.FileNumber.ToString()) + "," + Csv(p.FullName) + "," + Csv(p.NationalId) + "," + Csv(p.Mobile) + "," + Csv(p.City) + "," + Csv(p.StatusText)); File.WriteAllText(save.FileName, sb.ToString(), new UTF8Encoding(true)); database.Audit("تصدير قائمة المراجعين", "Export", Path.GetFileName(save.FileName), null, "CSV"); database.Checkpoint(); MessageBox.Show("تم التصدير.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                catch (Exception ex) { UiKit.ShowError(ex.Message); }
            }
        }
        private static string Csv(string s) { string v = s ?? ""; if (v.Length > 0 && (v[0] == '=' || v[0] == '+' || v[0] == '-' || v[0] == '@' || v[0] == '\t' || v[0] == '\r')) v = "'" + v; return "\"" + v.Replace("\"", "\"\"") + "\""; }
        private void ExportAppointmentsCsv(object sender, EventArgs e)
        {
            if (!session.IsAdmin) { UiKit.ShowError("تصدير البيانات متاح للمدير فقط."); return; }
            if (!UiKit.Confirm("سيحتوي الملف على بيانات مواعيد شخصية وغير مشفرة. هل تريد المتابعة؟", "تحذير خصوصية")) return;
            var rows = appointmentGrid.DataSource as BindingList<Appointment>; if (rows == null || rows.Count == 0) { UiKit.ShowError("لا توجد مواعيد ظاهرة للتصدير."); return; }
            using (var save = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "المواعيد_" + DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv" }) if (save.ShowDialog(this) == DialogResult.OK)
            {
                try { var sb = new StringBuilder(); sb.AppendLine("رقم الملف,اسم المراجع,العنوان,نوع الزيارة,التاريخ,الوقت,المدة,الحالة,الملاحظات"); foreach (Appointment a in rows) sb.AppendLine(Csv(a.FileNumber.ToString()) + "," + Csv(a.PatientName) + "," + Csv(a.Title) + "," + Csv(a.VisitType) + "," + Csv(a.StartsAt.ToString("yyyy/MM/dd")) + "," + Csv(a.TimeText) + "," + Csv(a.DurationMinutes.ToString()) + "," + Csv(a.Status) + "," + Csv(a.Notes)); File.WriteAllText(save.FileName, sb.ToString(), new UTF8Encoding(true)); database.Audit("تصدير المواعيد", "Export", Path.GetFileName(save.FileName), null, "الصفوف: " + rows.Count); database.Checkpoint(); MessageBox.Show("تم تصدير المواعيد الظاهرة.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                catch (Exception ex) { UiKit.ShowError(ex.Message); }
            }
        }
        private void ExportAuditCsv(object sender, EventArgs e)
        {
            if (!UiKit.Confirm("سجل العمليات قد يحتوي بيانات شخصية وغير مشفرة. هل تريد المتابعة؟", "تحذير خصوصية")) return;
            using (var save = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "سجل_العمليات_" + DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv" }) if (save.ShowDialog(this) == DialogResult.OK)
            {
                try { var sb = new StringBuilder(); sb.AppendLine("الوقت,الموظف,العملية,نوع السجل,رقم الملف,التفاصيل,الجهاز"); foreach (AuditEntry a in database.GetAllAudit()) sb.AppendLine(Csv(a.OccurredAt.ToString("yyyy/MM/dd HH:mm:ss")) + "," + Csv(a.UserName) + "," + Csv(a.Action) + "," + Csv(a.EntityType) + "," + Csv(a.FileNumber.HasValue ? a.FileNumber.Value.ToString() : "") + "," + Csv(a.Details) + "," + Csv(a.MachineName)); File.WriteAllText(save.FileName, sb.ToString(), new UTF8Encoding(true)); database.Audit("تصدير سجل العمليات", "Export", Path.GetFileName(save.FileName), null, "تصدير كامل"); database.Checkpoint(); MessageBox.Show("تم تصدير سجل العمليات كاملًا.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                catch (Exception ex) { UiKit.ShowError(ex.Message); }
            }
        }

        private void ManageUsers(object sender, EventArgs e) { using (var f = new UserManagementForm(security, session)) f.ShowDialog(this); }
        private void ChangePassword(object sender, EventArgs e) { using (var f = new ChangePasswordDialog(security, session)) f.ShowDialog(this); }
        private void ManageClosures(object sender, EventArgs e) { using (var f = new ClosureDatesForm(database)) f.ShowDialog(this); }
        private void OpenRecycleBin(object sender, EventArgs e) { using (var f = new RecycleBinForm(database)) f.ShowDialog(this); LoadAll(); }
        private void ShowAudit(object sender, EventArgs e)
        {
            var f = new Form { Text = "سجل العمليات — آخر 1000 عملية", RightToLeft = RightToLeft.Yes, RightToLeftLayout = true, StartPosition = FormStartPosition.CenterParent, Size = new Size(1000, 650), Font = UiKit.NormalFont };
            DataGridView g = UiKit.Grid(); UiKit.AddTextColumn(g, "OccurredAt", "الوقت", 18); UiKit.AddTextColumn(g, "UserName", "الموظف", 18); UiKit.AddTextColumn(g, "Action", "العملية", 22); UiKit.AddTextColumn(g, "FileNumber", "رقم الملف", 12); UiKit.AddTextColumn(g, "Details", "التفاصيل", 30); g.DataSource = new BindingList<AuditEntry>(database.GetRecentAudit(1000)); f.Controls.Add(g); f.ShowDialog(this); f.Dispose();
        }

        private void PrintPatientSummary(object sender, EventArgs e)
        {
            Patient p = SelectedPatient(patientGrid); if (p == null) { UiKit.ShowError("اختر مراجعًا أولًا."); return; } AppSettings set = database.GetSettings(); byte[] logo = database.GetClinicLogo(); List<Appointment> appointments = database.GetPatientAppointments(p.Id).Take(6).ToList(); List<PatientTask> tasks = database.GetPatientTasks(p.Id).Take(6).ToList();
            database.Audit("معاينة طباعة ملخص مراجع", "Print", p.Id.ToString(), p.FileNumber, p.FullName); database.Checkpoint();
            var doc = new PrintDocument { DocumentName = "ملخص_مراجع_" + p.FileNumber }; doc.PrintPage += delegate(object sender2, PrintPageEventArgs ev)
            {
                Rectangle r = ev.MarginBounds; using (var titleFont = new Font("Tahoma", 17, FontStyle.Bold)) using (var headFont = new Font("Tahoma", 11, FontStyle.Bold)) using (var textFont = new Font("Tahoma", 10)) using (var right = new StringFormat { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.DirectionRightToLeft }) using (var center = new StringFormat { Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.DirectionRightToLeft })
                {
                    int y = DrawClinicLogo(ev.Graphics, r, logo, r.Top); ev.Graphics.DrawString(set.ClinicName, titleFont, Brushes.Black, new RectangleF(r.Left, y, r.Width, 34), center); y += 42; ev.Graphics.DrawString("ملخص إداري للمراجع", headFont, Brushes.Black, new RectangleF(r.Left, y, r.Width, 28), center); y += 38;
                    string[] info = { "رقم الملف: " + p.FileNumber, "الاسم: " + p.FullName, "الهوية/الإقامة: " + p.NationalId, "الجوال: " + p.Mobile, "المدينة والعنوان: " + p.City + " — " + p.Address, "الحالة: " + p.StatusText, "آخر مراجعة مسجلة: " + (p.LastVisitAt.HasValue ? p.LastVisitAt.Value.ToString("yyyy/MM/dd") : "لا توجد") };
                    foreach (string line in info) DrawRightLine(ev.Graphics, line, textFont, r, ref y, right, 25);
                    y += 10; ev.Graphics.DrawString("آخر المواعيد", headFont, Brushes.Black, new RectangleF(r.Left, y, r.Width, 26), right); y += 28; foreach (Appointment a in appointments) DrawRightLine(ev.Graphics, a.DateText + " — " + a.TimeText + " — " + a.Title + " — " + a.Status, textFont, r, ref y, right, 24);
                    y += 8; ev.Graphics.DrawString("آخر المهام", headFont, Brushes.Black, new RectangleF(r.Left, y, r.Width, 26), right); y += 28; foreach (PatientTask t in tasks) DrawRightLine(ev.Graphics, t.DueText + " — " + t.Title + " — " + t.CompletionText, textFont, r, ref y, right, 24);
                }
            };
            using (var preview = new PrintPreviewDialog { Document = doc, Width = 1000, Height = 750, RightToLeft = RightToLeft.Yes }) preview.ShowDialog(this);
        }

        private void PrintAppointment(object s, EventArgs e)
        {
            Appointment a = SelectedAppointment(); if (a == null) { UiKit.ShowError("اختر موعدًا للطباعة."); return; } Patient p = database.GetPatient(a.PatientId); AppSettings set = database.GetSettings(); byte[] logo = database.GetClinicLogo();
            database.Audit("معاينة طباعة موعد", "Print", a.Id.ToString(), a.FileNumber, a.Title); database.Checkpoint();
            var doc = new PrintDocument { DocumentName = "موعد_" + a.FileNumber }; doc.PrintPage += delegate(object sender, PrintPageEventArgs ev)
            {
                Rectangle r = ev.MarginBounds; var titleFont = new Font("Tahoma", 18, FontStyle.Bold); var headFont = new Font("Tahoma", 12, FontStyle.Bold); var textFont = new Font("Tahoma", 11);
                var center = new StringFormat { Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.DirectionRightToLeft }; var right = new StringFormat { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.DirectionRightToLeft };
                int y = DrawClinicLogo(ev.Graphics, r, logo, r.Top); ev.Graphics.DrawString(set.ClinicName, titleFont, Brushes.Black, new RectangleF(r.Left, y, r.Width, 36), center); y += 48;
                ev.Graphics.DrawString("إشعار موعد", headFont, Brushes.Black, new RectangleF(r.Left, y, r.Width, 30), center); y += 55;
                string[] lines = { "رقم الملف: " + a.FileNumber, "اسم المراجع: " + a.PatientName, "رقم الهوية/الإقامة: " + (p == null ? "" : MaskIdentity(p.NationalId)), "نوع الموعد: " + a.Title + " - " + a.VisitType, "التاريخ الميلادي: " + a.DateText + " (" + SaudiValidation.ArabicDayName(a.StartsAt) + ")", "الوقت: " + a.TimeText, "مدة الموعد: " + a.DurationMinutes + " دقيقة", "الحالة: " + a.Status };
                foreach (string line in lines) DrawRightLine(ev.Graphics, line, textFont, r, ref y, right, 34);
                y += 25; ev.Graphics.DrawString("يرجى الحضور قبل الموعد بـ 15 دقيقة.", headFont, Brushes.Black, new RectangleF(r.Left, y, r.Width, 30), center); y += 55;
                ev.Graphics.DrawString(set.ClinicAddress + "   " + set.ClinicPhone, textFont, Brushes.Black, new RectangleF(r.Left, y, r.Width, 30), center);
                titleFont.Dispose(); headFont.Dispose(); textFont.Dispose(); center.Dispose(); right.Dispose();
            };
            using (var preview = new PrintPreviewDialog { Document = doc, Width = 1000, Height = 750, RightToLeft = RightToLeft.Yes }) preview.ShowDialog(this);
        }
        private static string MaskIdentity(string value) { string v = value ?? ""; return v.Length <= 4 ? v : new string('*', v.Length - 4) + v.Substring(v.Length - 4); }
        private static void DrawRightLine(Graphics graphics, string text, Font font, Rectangle bounds, ref int y, StringFormat format, int minimumHeight)
        {
            int height = Math.Max(minimumHeight, (int)Math.Ceiling(graphics.MeasureString(text ?? "", font, bounds.Width, format).Height) + 4); graphics.DrawString(text ?? "", font, Brushes.Black, new RectangleF(bounds.Left, y, bounds.Width, height), format); y += height;
        }
        private static int DrawClinicLogo(Graphics graphics, Rectangle bounds, byte[] logoBytes, int y)
        {
            if (logoBytes == null || logoBytes.Length == 0) return y;
            try
            {
                using (var stream = new MemoryStream(logoBytes)) using (Image logo = Image.FromStream(stream))
                {
                    float ratio = Math.Min(120f / logo.Width, 60f / logo.Height); float width = logo.Width * ratio, height = logo.Height * ratio; graphics.DrawImage(logo, bounds.Left + (bounds.Width - width) / 2f, y, width, height); return y + (int)height + 12;
                }
            }
            catch { return y; }
        }

        private void ConfigureNotification()
        {
            notify.Icon = SystemIcons.Information; notify.Visible = true; notify.Text = "سجلات المراجعين";
            var menu = new ContextMenuStrip(); menu.Items.Add("فتح البرنامج", null, delegate { UnlockAndShow(); }); ToolStripItem manualBackup = menu.Items.Add("نسخة احتياطية الآن", null, delegate { if (session.IsAdmin) RunScheduledBackup(true); else UiKit.ShowError("النسخ اليدوي متاح للمدير فقط."); }); manualBackup.Enabled = session.IsAdmin; menu.Items.Add("إنهاء البرنامج", null, delegate { forceExit = true; Close(); }); notify.ContextMenuStrip = menu; notify.DoubleClick += delegate { UnlockAndShow(); };
            notify.BalloonTipClicked += delegate { if (!UnlockAndShow()) return; if (notificationPatientId.HasValue) { Patient p = database.GetPatient(notificationPatientId.Value); if (p != null) OpenPatient(p); } };
            reminderTimer.Interval = 60000; reminderTimer.Tick += delegate { CheckReminders(); }; reminderTimer.Start();
            try { using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) if (run != null) run.SetValue("SaudiPatientRecords", "\"" + Application.ExecutablePath + "\""); } catch { }
        }
        private void CheckReminders()
        {
            try
            {
                DateTime now = DateTime.Now, soon = now.AddMinutes(5), missed = now.AddDays(-7); List<Appointment> appointments = database.GetUnnotifiedAppointments(missed, soon, 20); List<PatientTask> tasks = database.GetUnnotifiedTasks(missed, soon, 20);
                if (appointments.Count == 0 && tasks.Count == 0) return;
                Appointment firstAppointment = appointments.FirstOrDefault(); PatientTask firstTask = tasks.FirstOrDefault(); notificationPatientId = firstAppointment != null ? firstAppointment.PatientId : firstTask.PatientId;
                notify.BalloonTipTitle = "تنبيهات المواعيد والمهام";
                if (locked || !Visible) notify.BalloonTipText = "يوجد " + appointments.Count + " موعد و" + tasks.Count + " مهمة تحتاج المراجعة. افتح البرنامج للتفاصيل.";
                else if (firstAppointment != null) notify.BalloonTipText = firstAppointment.PatientName + " — " + firstAppointment.Title + " — " + firstAppointment.TimeText + (appointments.Count + tasks.Count > 1 ? " — وإجمالي " + (appointments.Count + tasks.Count) + " تنبيه" : "");
                else notify.BalloonTipText = firstTask.PatientName + " — " + firstTask.Title + (tasks.Count > 1 ? " — وإجمالي " + tasks.Count + " مهام" : "");
                notify.ShowBalloonTip(10000); foreach (Appointment a in appointments) database.MarkAppointmentNotified(a.Id); foreach (PatientTask t in tasks) database.MarkTaskNotified(t.Id);
            }
            catch { }
        }

        private void LockApplication() { if (locked) return; locked = true; Hide(); notify.BalloonTipTitle = "تم قفل البرنامج"; notify.BalloonTipText = "تم القفل تلقائيًا لحماية البيانات. انقر لفتح البرنامج."; notify.ShowBalloonTip(5000); }
        private bool UnlockAndShow()
        {
            if (locked) { using (var login = new LoginForm(security, session.Username)) { if (login.ShowDialog() != DialogResult.OK) return false; session = login.Session; database.SetCurrentSession(session.DisplayName, session.Role); security.FlushPendingAudit(database); locked = false; activity.Touch(); } }
            Show(); WindowState = FormWindowState.Maximized; Activate(); return true;
        }

        private void RunScheduledBackup(bool force)
        {
            AppSettings s = database.GetSettings(); if (!force && s.LastAutoBackupAt.HasValue && DateTime.Now - s.LastAutoBackupAt.Value < TimeSpan.FromHours(s.BackupIntervalHours)) return;
            try { string folder = string.IsNullOrWhiteSpace(s.AutoBackupDirectory) ? Path.Combine(database.DataDirectory, "AutoBackups") : s.AutoBackupDirectory; string path = backups.CreateBackup(folder, database); UpdateBackupStatus("نجحت في " + DateTime.Now.ToString("yyyy/MM/dd HH:mm") + " — " + Path.GetFileName(path), DateTime.Now); PruneBackups(folder); if (force) { notify.BalloonTipTitle = "النسخ الاحتياطي"; notify.BalloonTipText = "تم إنشاء النسخة بنجاح."; notify.ShowBalloonTip(5000); } }
            catch (Exception ex) { try { UpdateBackupStatus("فشلت: " + ex.Message, null); } catch { } notify.BalloonTipTitle = "فشل النسخ الاحتياطي"; notify.BalloonTipText = "افتح البرنامج لمراجعة حالة النسخ."; notify.ShowBalloonTip(8000); }
        }
        private void UpdateBackupStatus(string status, DateTime? successAt) { database.UpdateBackupStatus(status, successAt); backupStatus.Text = "حالة النسخ: " + status; }
        private static void PruneBackups(string folder) { foreach (FileInfo f in new DirectoryInfo(folder).GetFiles("*.zip").OrderByDescending(x => x.CreationTimeUtc).Skip(30)) f.Delete(); }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!forceExit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; locked = true; Hide(); notify.BalloonTipTitle = "البرنامج يعمل في الخلفية"; notify.BalloonTipText = "ستستمر تنبيهات المواعيد والمهام. استخدم أيقونة البرنامج بجانب الساعة للفتح أو الإنهاء."; notify.ShowBalloonTip(7000); return; }
            reminderTimer.Stop(); maintenanceTimer.Stop(); idleTimer.Stop(); try { RunScheduledBackup(true); } catch { } AppDatabase.CleanupTemporaryAttachments(); Application.RemoveMessageFilter(activity); notify.Visible = false; notify.Dispose();
        }

        private sealed class InactivityFilter : IMessageFilter
        {
            public DateTime LastActivity { get; private set; }
            public InactivityFilter() { LastActivity = DateTime.Now; }
            public void Touch() { LastActivity = DateTime.Now; }
            public bool PreFilterMessage(ref Message m) { if ((m.Msg >= 0x0100 && m.Msg <= 0x0109) || (m.Msg >= 0x0200 && m.Msg <= 0x020E)) Touch(); return false; }
        }
    }
}
