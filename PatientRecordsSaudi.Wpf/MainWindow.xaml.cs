using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;
using FluentWindow = global::Wpf.Ui.Controls.FluentWindow;

namespace PatientRecordsSaudi.Desktop;

public partial class MainWindow : FluentWindow
{
    private readonly AppDatabase database;
    private readonly BackupService backups;
    private readonly AppSecurity security;
    private readonly SecuritySession session;
    private readonly DispatcherTimer reminderTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly List<Button> navigationButtons;
    private readonly List<Border> pages;

    public MainWindow(AppDatabase database, BackupService backups, AppSecurity security, SecuritySession session)
    {
        this.database = database;
        this.backups = backups;
        this.security = security;
        this.session = session;
        InitializeComponent();

        navigationButtons = new() { DashboardNav, PatientsNav, AppointmentsNav, TasksNav, InventoryNav, SettingsNav };
        pages = new() { DashboardPage, PatientsPage, AppointmentsPage, TasksPage, InventoryPage, SettingsPage };
        SessionNameText.Text = session.DisplayName;
        TodayText.Text = SaudiValidation.ArabicDayName(DateTime.Today) + "، " + DateTime.Today.Day.ToString("00") + " " + SaudiValidation.MonthLabel(DateTime.Today.Month) + " " + DateTime.Today.Year;
        LoadSettings();
        LoadAll();

        reminderTimer.Tick += (_, _) => CheckReminders();
        reminderTimer.Start();
        Loaded += (_, _) =>
        {
            AnnualInventoryAlert();
            CheckReminders();
        };
        Closing += (_, _) => database.Checkpoint();
    }

    private void ShowPage(int index, string title)
    {
        for (int i = 0; i < pages.Count; i++) pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < navigationButtons.Count; i++) navigationButtons[i].Background = i == index ? new SolidColorBrush(Color.FromRgb(31, 113, 90)) : Brushes.Transparent;
        TopTitleText.Text = title;
    }

    private void DashboardNav_Click(object sender, RoutedEventArgs e) { LoadDashboard(); ShowPage(0, "الرئيسية"); }
    private void PatientsNav_Click(object sender, RoutedEventArgs e) { LoadPatients(); ShowPage(1, "المراجعون"); }
    private void AppointmentsNav_Click(object sender, RoutedEventArgs e) { LoadAppointments(); ShowPage(2, "المواعيد"); }
    private void TasksNav_Click(object sender, RoutedEventArgs e) { LoadTasks(); ShowPage(3, "المهام والتنبيهات"); }
    private void InventoryNav_Click(object sender, RoutedEventArgs e) { LoadInventory(); ShowPage(4, "الجرد السنوي"); }
    private void SettingsNav_Click(object sender, RoutedEventArgs e) { LoadSettings(); ShowPage(5, "الإعدادات"); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadAll();

    private void LoadAll()
    {
        LoadPatients();
        LoadAppointments();
        LoadTasks();
        LoadInventory();
        LoadDashboard();
    }

    private void LoadDashboard()
    {
        List<Appointment> today = database.GetAppointments(DateTime.Today, DateTime.Today.AddDays(1));
        List<Appointment> upcoming = database.GetAppointments(DateTime.Now, DateTime.Now.AddDays(30));
        List<PatientTask> tasks = database.GetTasks(false);
        List<Patient> inventory = database.GetInventoryCandidates(DateTime.Today);
        ActivePatientsCount.Text = database.CountActivePatients().ToString("N0");
        TodayAppointmentsCount.Text = today.Count.ToString("N0");
        UpcomingAppointmentsCount.Text = upcoming.Count.ToString("N0");
        OpenTasksCount.Text = tasks.Count.ToString("N0");
        InventoryCount.Text = inventory.Count.ToString("N0");
        DashboardTodayGrid.ItemsSource = new ObservableCollection<Appointment>(today);
        DashboardTasksGrid.ItemsSource = new ObservableCollection<PatientTask>(tasks.Take(30));
    }

    private void LoadPatients()
    {
        if (PatientsGrid is null || PatientSearchMode is null || PatientSortMode is null) return;
        PatientsGrid.ItemsSource = new ObservableCollection<Patient>(database.SearchPatients(ComboText(PatientSearchMode), PatientSearchText.Text, ShowArchivedCheck.IsChecked == true, ComboText(PatientSortMode)));
    }

    private void LoadAppointments()
    {
        if (AppointmentsGrid is null || AppointmentFilter is null) return;
        DateTime now = DateTime.Now;
        DateTime? from = null, to = null;
        switch (ComboText(AppointmentFilter))
        {
            case "القادمة": from = now; break;
            case "اليوم": from = DateTime.Today; to = DateTime.Today.AddDays(1); break;
            case "هذا الأسبوع": from = DateTime.Today; to = DateTime.Today.AddDays(7); break;
        }
        AppointmentsGrid.ItemsSource = new ObservableCollection<Appointment>(database.GetAppointments(from, to));
    }

    private void LoadTasks()
    {
        if (TasksGrid is null) return;
        TasksGrid.ItemsSource = new ObservableCollection<PatientTask>(database.GetTasks(ShowCompletedCheck.IsChecked == true));
    }

    private void LoadInventory()
    {
        if (InventoryGrid is null) return;
        InventoryGrid.ItemsSource = new ObservableCollection<Patient>(database.GetInventoryCandidates(DateTime.Today));
    }

    private void LoadSettings()
    {
        AppSettings settings = database.GetSettings();
        ClinicNameText.Text = settings.ClinicName;
        ClinicNameBox.Text = settings.ClinicName;
        ClinicPhoneBox.Text = settings.ClinicPhone;
        ClinicAddressBox.Text = settings.ClinicAddress;
        RequireLoginCheck.IsChecked = security.IsLoginRequired;
        Title = "نظام إدارة سجلات المراجعين — " + settings.ClinicName;
    }

    private static string ComboText(ComboBox combo)
    {
        return combo.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? string.Empty : combo.Text;
    }

    private void PatientSearch_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) LoadPatients(); }
    private void AppointmentFilter_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) LoadAppointments(); }
    private void ShowCompleted_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) LoadTasks(); }

    private bool GuardWrite()
    {
        if (!session.IsReadOnly) return true;
        ShowError("هذا المستخدم يملك صلاحية القراءة فقط.");
        return false;
    }

    private void NewPatient_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite()) return;
        var editor = new PatientEditorWindow(database, null, false) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is null) return;
        try
        {
            Patient patient = database.AddPatient(editor.Result);
            LoadAll();
            MessageBox.Show("تم إنشاء ملف المراجع بنجاح.\nرقم الملف: " + patient.FileNumber, "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
        catch (DuplicatePatientException ex)
        {
            if (Confirm(ex.Message + "\nهل تريد فتح الملف الموجود؟")) OpenPatient(ex.ExistingPatient);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void EditPatient_Click(object sender, RoutedEventArgs e) => OpenPatient(SelectedPatient(PatientsGrid));
    private void EditInventoryPatient_Click(object sender, RoutedEventArgs e) => OpenPatient(SelectedPatient(InventoryGrid));
    private void PatientsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenPatient(SelectedPatient(PatientsGrid));
    private void InventoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenPatient(SelectedPatient(InventoryGrid));

    private Patient? SelectedPatient(DataGrid grid) => grid.SelectedItem is Patient patient ? database.GetPatient(patient.Id) : null;

    private Patient? PatientFromRow(object data)
    {
        return data switch
        {
            Patient patient => database.GetPatient(patient.Id),
            Appointment appointment => database.GetPatient(appointment.PatientId),
            PatientTask task => database.GetPatient(task.PatientId),
            _ => null
        };
    }

    private void OpenPatientFromRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) OpenPatient(PatientFromRow(button.DataContext));
    }

    private void OpenPatient(Patient? patient)
    {
        if (patient is null) { ShowError("اختر مراجعًا أولًا."); return; }
        bool readOnly = session.IsReadOnly || patient.IsArchived;
        var editor = new PatientEditorWindow(database, patient, readOnly) { Owner = this };
        if (editor.ShowDialog() == true && editor.Result is not null && !readOnly)
        {
            try { database.UpdatePatient(editor.Result); LoadAll(); }
            catch (Exception ex) { ShowError(ex.Message); }
        }
    }

    private void ArchivePatient_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite()) return;
        Patient? patient = SelectedPatient(PatientsGrid);
        if (patient is null) { ShowError("اختر مراجعًا أولًا."); return; }
        try
        {
            if (patient.IsArchived)
            {
                if (Confirm("هل تريد استعادة الملف رقم " + patient.FileNumber + "؟")) database.RestorePatient(patient.Id);
            }
            else if (Confirm("سيُؤرشف الملف دون إعادة استخدام رقمه، وتُغلق عناصره المستقبلية. هل تريد المتابعة؟"))
            {
                database.ArchivePatient(patient.Id, "أرشفة يدوية", true);
            }
            LoadAll();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void NewAppointmentForPatient_Click(object sender, RoutedEventArgs e)
    {
        Patient? patient = SelectedPatient(PatientsGrid);
        if (patient is null) { ShowError("اختر مراجعًا أولًا."); return; }
        ShowAppointmentEditor(null, patient.FileNumber);
    }

    private void NewAppointment_Click(object sender, RoutedEventArgs e) => ShowAppointmentEditor(null, null);
    private void EditAppointment_Click(object sender, RoutedEventArgs e)
    {
        Appointment? selected = AppointmentsGrid.SelectedItem as Appointment;
        if (selected is null) { ShowError("اختر موعدًا أولًا."); return; }
        ShowAppointmentEditor(database.GetAppointment(selected.Id), null);
    }

    private void ShowAppointmentEditor(Appointment? appointment, long? initialFileNumber)
    {
        if (!GuardWrite()) return;
        var editor = new AppointmentEditorWindow(database, appointment, initialFileNumber) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is null) return;
        try
        {
            if (appointment is null) database.AddAppointment(editor.Result); else database.UpdateAppointment(editor.Result);
            LoadAll();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void DeleteAppointment_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite()) return;
        Appointment? appointment = AppointmentsGrid.SelectedItem as Appointment;
        if (appointment is null) { ShowError("اختر موعدًا أولًا."); return; }
        try { if (Confirm("نقل الموعد المحدد إلى المحذوفات مع إمكانية استعادته؟")) { database.DeleteAppointment(appointment.Id); LoadAll(); } }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void PrintAppointment_Click(object sender, RoutedEventArgs e)
    {
        Appointment? appointment = AppointmentsGrid.SelectedItem as Appointment;
        if (appointment is null) { ShowError("اختر موعدًا أولًا."); return; }
        AppSettings settings = database.GetSettings();
        var document = new FlowDocument { FlowDirection = FlowDirection.RightToLeft, FontFamily = new FontFamily("Segoe UI"), FontSize = 15, PagePadding = new Thickness(55) };
        document.Blocks.Add(new Paragraph(new Run(settings.ClinicName)) { FontSize = 24, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center });
        document.Blocks.Add(new Paragraph(new Run("إشعار موعد")) { FontSize = 20, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center });
        document.Blocks.Add(new Paragraph(new Run("اسم المراجع: " + appointment.PatientName)));
        document.Blocks.Add(new Paragraph(new Run("رقم الملف: " + appointment.FileNumber)));
        document.Blocks.Add(new Paragraph(new Run("الموعد: " + appointment.Title)));
        document.Blocks.Add(new Paragraph(new Run("التاريخ الميلادي: " + appointment.DateText)));
        document.Blocks.Add(new Paragraph(new Run("الوقت: " + appointment.TimeText)));
        document.Blocks.Add(new Paragraph(new Run("نوع الزيارة: " + appointment.VisitType)));
        if (!string.IsNullOrWhiteSpace(settings.ClinicPhone)) document.Blocks.Add(new Paragraph(new Run("هاتف المنشأة: " + settings.ClinicPhone)));
        var print = new System.Windows.Controls.PrintDialog();
        if (print.ShowDialog() == true) print.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "موعد " + appointment.FileNumber);
    }

    private void NewTask_Click(object sender, RoutedEventArgs e) => ShowTaskEditor(null, null);
    private void EditTask_Click(object sender, RoutedEventArgs e)
    {
        PatientTask? selected = TasksGrid.SelectedItem as PatientTask;
        if (selected is null) { ShowError("اختر مهمة أولًا."); return; }
        ShowTaskEditor(selected, null);
    }

    private void ShowTaskEditor(PatientTask? task, long? fileNumber)
    {
        if (!GuardWrite()) return;
        var editor = new TaskEditorWindow(database, task, fileNumber) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is null) return;
        try { if (task is null) database.AddTask(editor.Result); else database.UpdateTask(editor.Result); LoadAll(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ToggleTask_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite()) return;
        PatientTask? task = TasksGrid.SelectedItem as PatientTask;
        if (task is null) { ShowError("اختر مهمة أولًا."); return; }
        try { task.IsCompleted = !task.IsCompleted; database.UpdateTask(task); LoadAll(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite()) return;
        PatientTask? task = TasksGrid.SelectedItem as PatientTask;
        if (task is null) { ShowError("اختر مهمة أولًا."); return; }
        try { if (Confirm("نقل المهمة المحددة إلى المحذوفات مع إمكانية استعادتها؟")) { database.DeleteTask(task.Id); LoadAll(); } }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ArchiveInventory_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite()) return;
        Patient? patient = SelectedPatient(InventoryGrid);
        if (patient is null) { ShowError("اختر مراجعًا من قائمة الجرد."); return; }
        try
        {
            if (Confirm("هل راجعت الالتزامات النظامية وتريد أرشفة الملف رقم " + patient.FileNumber + "؟"))
            {
                database.ArchivePatient(patient.Id, "جرد سنوي: عدم مراجعة لمدة 10 سنوات", true);
                LoadAll();
            }
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite()) return;
        if (string.IsNullOrWhiteSpace(ClinicNameBox.Text)) { ShowError("اسم المنشأة مطلوب."); return; }
        try
        {
            AppSettings settings = database.GetSettings();
            settings.ClinicName = ClinicNameBox.Text.Trim();
            settings.ClinicPhone = ClinicPhoneBox.Text.Trim();
            settings.ClinicAddress = ClinicAddressBox.Text.Trim();
            database.SaveSettings(settings);
            security.SetLoginRequired(session, RequireLoginCheck.IsChecked == true);
            LoadSettings();
            MessageBox.Show("تم حفظ الإعدادات.", "تم", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
        catch (Exception ex)
        {
            RequireLoginCheck.IsChecked = security.IsLoginRequired;
            ShowError(ex.Message);
        }
    }

    private void ManageUsers_Click(object sender, RoutedEventArgs e)
    {
        try { new AccountManagerWindow(security, session) { Owner = this }.ShowDialog(); RequireLoginCheck.IsChecked = security.IsLoginRequired; }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite()) return;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "اختر مجلد حفظ النسخة الاحتياطية" };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        try
        {
            string path = backups.CreateBackup(dialog.SelectedPath, database);
            MessageBox.Show("تم إنشاء النسخة الاحتياطية:\n" + path, "نجح النسخ", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite()) return;
        var dialog = new OpenFileDialog { Filter = "نسخة سجلات المراجعين (*.zip)|*.zip", Title = "اختر النسخة الاحتياطية" };
        if (dialog.ShowDialog(this) != true || !Confirm("ستستبدل النسخة الحالية بعد إنشاء نسخة أمان داخلية. هل تريد المتابعة؟")) return;
        try
        {
            backups.RestoreBackup(dialog.FileName, database);
            MessageBox.Show("تمت الاستعادة. سيُغلق البرنامج الآن؛ افتحه مجددًا لتحميل البيانات المستعادة.", "تمت الاستعادة", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex) { try { database.Reopen(); } catch { } ShowError(ex.Message); }
    }

    private void Closures_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite()) return;
        new ClosureDatesWindow(database) { Owner = this }.ShowDialog();
    }

    private void RecycleBin_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite()) return;
        new RecycleBinWindow(database) { Owner = this }.ShowDialog();
        LoadAll();
    }

    private void AnnualInventoryAlert()
    {
        try
        {
            AppSettings settings = database.GetSettings();
            if (settings.LastInventoryAlertYear == DateTime.Today.Year) return;
            List<Patient> candidates = database.GetInventoryCandidates(DateTime.Today);
            database.SetInventoryAlerted(DateTime.Today.Year);
            if (candidates.Count > 0)
            {
                ShowPage(4, "الجرد السنوي");
                MessageBox.Show("تنبيه الجرد السنوي: يوجد " + candidates.Count + " ملفًا لم يسجل له نشاط منذ عشر سنوات أو أكثر. راجع القائمة قبل الأرشفة.", "الجرد السنوي", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            }
        }
        catch { }
    }

    private void CheckReminders()
    {
        try
        {
            DateTime now = DateTime.Now;
            List<Appointment> appointments = database.GetUnnotifiedAppointments(now.AddMinutes(-1), now.AddMinutes(15), 5);
            List<PatientTask> tasks = database.GetUnnotifiedTasks(now.AddMinutes(-1), now.AddMinutes(15), 5);
            if (appointments.Count == 0 && tasks.Count == 0) return;
            foreach (Appointment item in appointments) database.MarkAppointmentNotified(item.Id);
            foreach (PatientTask item in tasks) database.MarkTaskNotified(item.Id);
            string text = string.Join("\n", appointments.Select(x => "موعد: " + x.PatientName + " — " + x.Title + " — " + x.TimeText)
                .Concat(tasks.Select(x => "مهمة: " + x.PatientName + " — " + x.Title)));
            MessageBox.Show(text, "تنبيهات قريبة", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
        catch { }
    }

    private static bool Confirm(string text) => MessageBox.Show(text, "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.RtlReading) == MessageBoxResult.Yes;
    private static void ShowError(string text) => MessageBox.Show(text, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
}
