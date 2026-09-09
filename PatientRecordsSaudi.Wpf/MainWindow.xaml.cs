using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private readonly DispatcherTimer maintenanceTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    private readonly List<Button> navigationButtons;
    private readonly List<Border> pages;
    private string autoBackupDirectory = string.Empty;
    private ReminderWindow? activeReminder;

    public MainWindow(AppDatabase database, BackupService backups, AppSecurity security, SecuritySession session)
    {
        this.database = database;
        this.backups = backups;
        this.security = security;
        this.session = session;
        InitializeComponent();
        InitializeSettingsChoices();

        navigationButtons = new() { DashboardNav, PatientsNav, AppointmentsNav, TasksNav, InventoryNav, SettingsNav };
        pages = new() { DashboardPage, PatientsPage, AppointmentsPage, TasksPage, InventoryPage, SettingsPage };
        SessionNameText.Text = session.DisplayName;
        TodayText.Text = SaudiValidation.ArabicDayName(DateTime.Today) + "، " + DateTime.Today.Day.ToString("00") + " " + SaudiValidation.MonthLabel(DateTime.Today.Month) + " " + DateTime.Today.Year;
        LoadSettings();
        LoadAll();

        reminderTimer.Tick += (_, _) => CheckReminders();
        reminderTimer.Start();
        maintenanceTimer.Tick += (_, _) => RunScheduledBackup(false);
        maintenanceTimer.Start();
        Loaded += (_, _) =>
        {
            ApplyResponsiveLayout();
            AnnualInventoryAlert();
            CheckReminders();
            RunScheduledBackup(false);
        };
        Closing += (_, _) => { reminderTimer.Stop(); maintenanceTimer.Stop(); database.Checkpoint(); };
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
        SetEmptyState(DashboardTodayEmpty, today.Count == 0);
        SetEmptyState(DashboardTasksEmpty, tasks.Count == 0);
    }

    private void LoadPatients()
    {
        if (PatientsGrid is null || PatientSearchMode is null || PatientSortMode is null) return;
        List<Patient> patients = database.SearchPatients(ComboText(PatientSearchMode), PatientSearchText.Text, ShowArchivedCheck.IsChecked == true, ComboText(PatientSortMode));
        PatientsGrid.ItemsSource = new ObservableCollection<Patient>(patients);
        SetEmptyState(PatientsEmpty, patients.Count == 0);
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
        List<Appointment> appointments = database.GetAppointments(from, to);
        AppointmentsGrid.ItemsSource = new ObservableCollection<Appointment>(appointments);
        SetEmptyState(AppointmentsEmpty, appointments.Count == 0);
    }

    private void LoadTasks()
    {
        if (TasksGrid is null) return;
        List<PatientTask> tasks = database.GetTasks(ShowCompletedCheck.IsChecked == true);
        TasksGrid.ItemsSource = new ObservableCollection<PatientTask>(tasks);
        SetEmptyState(TasksEmpty, tasks.Count == 0);
    }

    private void LoadInventory()
    {
        if (InventoryGrid is null) return;
        List<Patient> inventory = database.GetInventoryCandidates(DateTime.Today);
        InventoryGrid.ItemsSource = new ObservableCollection<Patient>(inventory);
        SetEmptyState(InventoryEmpty, inventory.Count == 0);
    }

    private void LoadSettings()
    {
        AppSettings settings = database.GetSettings();
        ClinicNameText.Text = settings.ClinicName;
        ClinicNameBox.Text = settings.ClinicName;
        ClinicPhoneBox.Text = settings.ClinicPhone;
        ClinicAddressBox.Text = settings.ClinicAddress;
        SelectSettingOption(WorkStartBox, settings.WorkDayStartMinutes);
        SelectSettingOption(WorkEndBox, settings.WorkDayEndMinutes);
        SelectSettingOption(DefaultDurationBox, settings.DefaultAppointmentMinutes);
        SelectSettingOption(BackupIntervalBox, settings.BackupIntervalHours);
        autoBackupDirectory = settings.AutoBackupDirectory ?? string.Empty;
        RefreshBackupDirectoryText();
        VisitTypesText.Text = JoinLines(settings.VisitTypes);
        AppointmentStatusesText.Text = JoinLines(settings.AppointmentStatuses);
        TaskPrioritiesText.Text = JoinLines(settings.TaskPriorities);
        GenderOptionsText.Text = JoinLines(settings.GenderOptions);
        BloodTypesText.Text = JoinLines(settings.BloodTypes);
        ClinicLogoStatusText.Text = string.IsNullOrWhiteSpace(settings.ClinicLogoStoredId) ? "لا يوجد شعار حالي" : "الشعار الحالي: " + settings.ClinicLogoFileName;
        BackupStatusText.Text = "حالة آخر نسخة: " + settings.LastBackupStatus;
        RequireLoginCheck.IsChecked = security.IsLoginRequired;
        Title = "نظام إدارة سجلات المراجعين — " + settings.ClinicName;
        SetSettingsEnabled(session.IsAdmin);
    }

    private void InitializeSettingsChoices()
    {
        var times = new List<SettingOption>();
        for (int minutes = 0; minutes <= 24 * 60; minutes += 30)
        {
            int hour = (minutes / 60) % 24;
            int minute = minutes % 60;
            int displayHour = hour % 12;
            if (displayHour == 0) displayHour = 12;
            times.Add(new SettingOption(minutes, $"{displayHour:00}:{minute:00} {(hour >= 12 ? "م" : "ص")}"));
        }
        WorkStartBox.ItemsSource = times.Where(x => x.Value < 24 * 60).ToList();
        WorkEndBox.ItemsSource = times.Where(x => x.Value > 0).ToList();
        DefaultDurationBox.ItemsSource = new[] { 15, 20, 30, 45, 60, 90, 120 }.Select(x => new SettingOption(x, x + " دقيقة")).ToList();
        BackupIntervalBox.ItemsSource = new[] { 1, 2, 4, 6, 8, 12, 24 }.Select(x => new SettingOption(x, x == 1 ? "كل ساعة" : "كل " + x + " ساعات")).ToList();
    }

    private static void SelectSettingOption(ComboBox combo, int value)
    {
        combo.SelectedItem = combo.Items.Cast<SettingOption>().FirstOrDefault(x => x.Value == value);
        if (combo.SelectedItem is null && combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static int SelectedSettingValue(ComboBox combo, string fieldName)
    {
        if (combo.SelectedItem is SettingOption option) return option.Value;
        throw new InvalidOperationException("اختر " + fieldName + ".");
    }

    private static void SetEmptyState(TextBlock text, bool empty) => text.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    private static string JoinLines(IEnumerable<string>? values) => string.Join(Environment.NewLine, values ?? Array.Empty<string>());
    private static List<string> LookupLines(TextBox box) => box.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private void SetSettingsEnabled(bool enabled)
    {
        foreach (Control control in new Control[] { ClinicNameBox, ClinicPhoneBox, ClinicAddressBox, WorkStartBox, WorkEndBox, DefaultDurationBox, BackupIntervalBox, VisitTypesText, AppointmentStatusesText, TaskPrioritiesText, GenderOptionsText, BloodTypesText, RequireLoginCheck, ChooseLogoButton, RemoveLogoButton, InternalBackupFolderButton, ChooseBackupFolderButton, ManageUsersButton, CreateBackupButton, RestoreBackupButton, ClosuresButton, RecycleBinButton, SaveSettingsButton })
            control.IsEnabled = enabled;
    }

    private void RefreshBackupDirectoryText()
    {
        AutoBackupDirectoryBox.Text = string.IsNullOrWhiteSpace(autoBackupDirectory)
            ? "المجلد الداخلي الآمن داخل بيانات التطبيق"
            : autoBackupDirectory;
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();

    private void ApplyResponsiveLayout()
    {
        if (DashboardTablesGrid is null || SettingsActionsGrid is null) return;
        bool compact = ActualWidth < 1280;
        DashboardTablesGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        DashboardTablesGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(2, GridUnitType.Star);
        Grid.SetColumn(DashboardAppointmentsCard, 0);
        Grid.SetRow(DashboardAppointmentsCard, 0);
        Grid.SetColumn(DashboardTasksCard, compact ? 0 : 1);
        Grid.SetRow(DashboardTasksCard, compact ? 1 : 0);

        SettingsActionsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        SettingsActionsGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(SecuritySettingsCard, 0);
        Grid.SetRow(SecuritySettingsCard, 0);
        Grid.SetColumn(BackupSettingsCard, compact ? 0 : 1);
        Grid.SetRow(BackupSettingsCard, compact ? 1 : 0);
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
        byte[]? logo = database.GetClinicLogo();
        if (logo is { Length: > 0 })
        {
            using var stream = new MemoryStream(logo);
            var imageSource = new BitmapImage();
            imageSource.BeginInit();
            imageSource.CacheOption = BitmapCacheOption.OnLoad;
            imageSource.StreamSource = stream;
            imageSource.EndInit();
            imageSource.Freeze();
            document.Blocks.Add(new BlockUIContainer(new System.Windows.Controls.Image { Source = imageSource, Width = 96, Height = 96, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center }));
        }
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
            settings.WorkDayStartMinutes = SelectedSettingValue(WorkStartBox, "بداية الدوام");
            settings.WorkDayEndMinutes = SelectedSettingValue(WorkEndBox, "نهاية الدوام");
            settings.DefaultAppointmentMinutes = SelectedSettingValue(DefaultDurationBox, "مدة الموعد الافتراضية");
            settings.BackupIntervalHours = SelectedSettingValue(BackupIntervalBox, "فترة النسخ الاحتياطي");
            settings.AutoBackupDirectory = autoBackupDirectory;
            settings.VisitTypes = LookupLines(VisitTypesText);
            settings.AppointmentStatuses = LookupLines(AppointmentStatusesText);
            settings.TaskPriorities = LookupLines(TaskPrioritiesText);
            settings.GenderOptions = LookupLines(GenderOptionsText);
            settings.BloodTypes = LookupLines(BloodTypesText);
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

    private void ChooseAutoBackupDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite()) return;
        var dialog = new OpenFolderDialog { Title = "اختر مجلدًا على قرص خارجي أو موقع نسخ آمن", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        autoBackupDirectory = dialog.FolderName;
        RefreshBackupDirectoryText();
    }

    private void ResetAutoBackupDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite()) return;
        autoBackupDirectory = string.Empty;
        RefreshBackupDirectoryText();
    }

    private void ChooseClinicLogo_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite()) return;
        var dialog = new OpenFileDialog { Filter = "صور الشعار|*.png;*.jpg;*.jpeg", Title = "اختر شعار المنشأة" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            using (var input = File.OpenRead(dialog.FileName))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0 || decoder.Frames[0].PixelWidth < 32 || decoder.Frames[0].PixelHeight < 32)
                    throw new InvalidOperationException("أبعاد الشعار صغيرة جدًا؛ الحد الأدنى 32×32 بكسل.");
            }
            database.SetClinicLogo(dialog.FileName);
            ClinicLogoStatusText.Text = "الشعار الحالي: " + Path.GetFileName(dialog.FileName);
        }
        catch (Exception ex) { ShowError("تعذر حفظ الشعار: " + ex.Message); }
    }

    private void RemoveClinicLogo_Click(object sender, RoutedEventArgs e)
    {
        if (!GuardWrite() || !Confirm("إزالة شعار المنشأة من الطباعة؟")) return;
        try { database.RemoveClinicLogo(); ClinicLogoStatusText.Text = "لا يوجد شعار حالي"; }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!session.IsAdmin) { ShowError("إنشاء النسخة الاحتياطية متاح للمدير فقط."); return; }
        var dialog = new OpenFolderDialog { Title = "اختر مجلد حفظ النسخة الاحتياطية", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            string path = backups.CreateBackup(dialog.FolderName, database);
            database.UpdateBackupStatus("نجحت في " + DateTime.Now.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) + " — " + Path.GetFileName(path), DateTime.Now);
            LoadSettings();
            MessageBox.Show("تم إنشاء النسخة الاحتياطية:\n" + path, "نجح النسخ", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
        catch (Exception ex) { try { database.UpdateBackupStatus("فشلت: " + ex.Message, null); } catch { } ShowError(ex.Message); }
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!session.IsAdmin) { ShowError("استعادة النسخة الاحتياطية متاحة للمدير فقط."); return; }
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

    private void RunScheduledBackup(bool force)
    {
        if (!session.IsAdmin) return;
        try
        {
            AppSettings settings = database.GetSettings();
            if (!force && settings.LastAutoBackupAt.HasValue && DateTime.Now - settings.LastAutoBackupAt.Value < TimeSpan.FromHours(settings.BackupIntervalHours)) return;
            string folder = string.IsNullOrWhiteSpace(settings.AutoBackupDirectory) ? Path.Combine(database.DataDirectory, "AutoBackups") : settings.AutoBackupDirectory;
            string path = backups.CreateBackup(folder, database);
            database.UpdateBackupStatus("نجحت في " + DateTime.Now.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) + " — " + Path.GetFileName(path), DateTime.Now);
            PruneBackups(folder);
            if (SettingsPage.Visibility == Visibility.Visible) LoadSettings();
        }
        catch (Exception ex)
        {
            try { database.UpdateBackupStatus("فشلت: " + ex.Message, null); } catch { }
            if (SettingsPage.Visibility == Visibility.Visible) BackupStatusText.Text = "حالة آخر نسخة: فشلت — " + ex.Message;
        }
    }

    private static void PruneBackups(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (FileInfo file in new DirectoryInfo(folder).GetFiles("نسخة_سجلات_المراجعين_*.zip").OrderByDescending(x => x.CreationTimeUtc).Skip(30))
            file.Delete();
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
            List<Appointment> appointments = database.GetUnnotifiedAppointments(now, now.AddDays(2), 50);
            List<PatientTask> tasks = database.GetUnnotifiedTasks(now.AddDays(-7), now.AddMinutes(5), 50);
            if (appointments.Count == 0 && tasks.Count == 0 || activeReminder is not null) return;
            activeReminder = new ReminderWindow(appointments, tasks, patientId => OpenPatient(database.GetPatient(patientId))) { Owner = this };
            activeReminder.Closed += (_, _) => activeReminder = null;
            activeReminder.Show();
            foreach (Appointment item in appointments) database.MarkAppointmentNotified(item.Id);
            foreach (PatientTask item in tasks) database.MarkTaskNotified(item.Id);
        }
        catch (Exception ex)
        {
            try { database.Audit("خطأ في نافذة التنبيهات", "System", "reminders", null, ex.Message); database.Checkpoint(); } catch { }
        }
    }

    private static bool Confirm(string text) => MessageBox.Show(text, "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.RtlReading) == MessageBoxResult.Yes;
    private static void ShowError(string text) => MessageBox.Show(text, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);

    private sealed record SettingOption(int Value, string Label)
    {
        public override string ToString() => Label;
    }
}
