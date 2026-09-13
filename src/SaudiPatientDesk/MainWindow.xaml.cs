using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.RegularExpressions;
using SaudiPatientDesk.Domain;
using SaudiPatientDesk.Services;

namespace SaudiPatientDesk;

public partial class MainWindow : Window
{
    private readonly PatientService _patients = new();
    private readonly AppointmentService _appointments = new();
    private readonly SettingsService _settings = new();
    private readonly AppointmentPrintService _printer = new();
    private Patient? _editingPatient;

    public MainWindow()
    {
        InitializeComponent();
        ShowPanel("dashboard");
        ClinicNameBox.Text = _settings.Get("clinic_name", "");
        DataLocationBox.Text = _settings.DataLocation;
        InitializeAppointmentSelectors();
        RefreshAll();
    }

    private void InitializeAppointmentSelectors()
    {
        var tomorrow = DateTime.Today.AddDays(1);
        AppointmentDayBox.ItemsSource = Enumerable.Range(1, 31);
        AppointmentMonthBox.ItemsSource = Enumerable.Range(1, 12);
        AppointmentYearBox.ItemsSource = Enumerable.Range(DateTime.Today.Year, 21);
        AppointmentHourBox.ItemsSource = Enumerable.Range(0, 24).Select(x => x.ToString("00"));
        AppointmentMinuteBox.ItemsSource = Enumerable.Range(0, 60).Select(x => x.ToString("00"));
        AppointmentDayBox.SelectedItem = tomorrow.Day;
        AppointmentMonthBox.SelectedItem = tomorrow.Month;
        AppointmentYearBox.SelectedItem = tomorrow.Year;
        AppointmentHourBox.SelectedItem = "09";
        AppointmentMinuteBox.SelectedItem = "00";
    }

    private void RefreshAll()
    {
        var snapshot = _appointments.Snapshot();
        PatientsCount.Text = snapshot.ActivePatients.ToString("N0");
        TodayCount.Text = snapshot.TodayAppointments.ToString("N0");
        AlertsCount.Text = snapshot.UpcomingAlerts.ToString("N0");
        InactiveCount.Text = snapshot.InactiveTenYears.ToString("N0");
        var upcoming = _appointments.Upcoming();
        DashboardAppointmentsGrid.ItemsSource = upcoming;
        AppointmentsGrid.ItemsSource = upcoming;
        PatientsGrid.ItemsSource = _patients.Search(PatientFilter.Text);
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        var key = (sender as Button)?.Tag?.ToString();
        ShowPanel(key);
    }

    private void ShowPanel(string? key)
    {
        DashboardPanel.Visibility = key == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        PatientsPanel.Visibility = key == "patients" ? Visibility.Visible : Visibility.Collapsed;
        AppointmentsPanel.Visibility = key == "appointments" ? Visibility.Visible : Visibility.Collapsed;
        SearchPanel.Visibility = key == "search" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = key == "settings" ? Visibility.Visible : Visibility.Collapsed;
        (PageTitle.Text, PageSubtitle.Text) = key switch
        {
            "patients" => ("المرضى", "إضافة السجلات وتعديلها والبحث فيها"),
            "appointments" => ("المواعيد", "المواعيد القادمة ومنع التعارض تلقائياً"),
            "search" => ("البحث الشامل", "الوصول السريع إلى ملف المريض"),
            "settings" => ("الإعدادات", "إعدادات المنشأة والنسخ الاحتياطي"),
            _ => ("لوحة التحكم", "ملخص العمل والمواعيد القادمة")
        };
    }

    private void AddPatient_Click(object sender, RoutedEventArgs e)
    {
        _editingPatient = null;
        PatientFormTitle.Text = "إضافة مريض جديد";
        PatientFormSubtitle.Text = "سيُنشأ رقم الملف تلقائياً";
        SavePatientButton.Content = "إضافة المريض";
        PatientFullNameBox.Clear();
        PatientNationalIdBox.Clear();
        PatientMobileBox.Clear();
        PatientSecondaryBox.Clear();
        PatientMedicalBox.Clear();
        PatientFormOverlay.Visibility = Visibility.Visible;
        PatientFullNameBox.Focus();
    }

    private void AddAppointment_Click(object sender, RoutedEventArgs e)
    {
        AppointmentFileNumberBox.Clear();
        AppointmentNotesBox.Clear();
        InitializeAppointmentSelectors();
        ClearAppointmentPatientCard("لم يتم اختيار مراجع");
        AppointmentFormOverlay.Visibility = Visibility.Visible;
        AppointmentFileNumberBox.Focus();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAll();
    private void PatientFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (PatientsGrid is not null) PatientsGrid.ItemsSource = _patients.Search(PatientFilter.Text);
    }

    private void EditPatient_Click(object sender, RoutedEventArgs e) => EditSelected(PatientsGrid.SelectedItem as Patient);
    private void PatientsGrid_DoubleClick(object sender, MouseButtonEventArgs e) => EditSelected(PatientsGrid.SelectedItem as Patient);
    private void SearchResultsGrid_DoubleClick(object sender, MouseButtonEventArgs e) => EditSelected(SearchResultsGrid.SelectedItem as Patient);

    private void EditSelected(Patient? patient)
    {
        if (patient is null) return;
        _editingPatient = patient;
        PatientFormTitle.Text = "تعديل ملف المريض";
        PatientFormSubtitle.Text = $"رقم الملف: {patient.FileNumber}";
        SavePatientButton.Content = "حفظ التعديلات";
        PatientFullNameBox.Text = patient.FullName;
        PatientNationalIdBox.Text = patient.NationalId;
        PatientMobileBox.Text = patient.Mobile;
        PatientSecondaryBox.Text = patient.SecondaryContact;
        PatientMedicalBox.Text = patient.BriefMedicalInfo;
        PatientFormOverlay.Visibility = Visibility.Visible;
        PatientFullNameBox.Focus();
    }

    private void DeletePatient_Click(object sender, RoutedEventArgs e)
    {
        if (PatientsGrid.SelectedItem is not Patient patient) return;
        if (MessageBox.Show($"نقل ملف {patient.FileNumber} إلى المحذوفات؟\nلن يُعاد استخدام رقم الملف.",
            "تأكيد الحذف الآمن", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _patients.SoftDelete(patient.Id);
        RefreshAll();
    }

    private void RunSearch_Click(object sender, RoutedEventArgs e) => SearchResultsGrid.ItemsSource = _patients.Search(GlobalSearchBox.Text, 10000);
    private void GlobalSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) RunSearch_Click(sender, e);
    }

    private void AppointmentGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is Appointment item)
            PrintAppointment(item);
    }

    private void PrintSelectedAppointment_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is Appointment item) PrintAppointment(item);
    }

    private void PrintSelectedAppointment_Click(object sender, RoutedEventArgs e)
    {
        if (AppointmentsGrid.SelectedItem is not Appointment item)
        {
            MessageBox.Show("حدد موعداً من الجدول أولاً.", "طباعة الموعد", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        PrintAppointment(item);
    }

    private void PrintAppointment(Appointment appointment)
    {
        var patient = _patients.FindByFileNumber(appointment.FileNumber);
        if (patient is null) return;
        _printer.Print(appointment, patient, _settings.Get("clinic_name", "نظام سجلات المرضى"));
    }

    private void SavePatientInline_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var draft = new PatientDraft(PatientFullNameBox.Text, PatientNationalIdBox.Text, PatientMobileBox.Text,
                PatientSecondaryBox.Text, PatientMedicalBox.Text);
            if (_editingPatient is null)
            {
                var created = _patients.Add(draft);
                MessageBox.Show($"تمت إضافة المريض. رقم الملف: {created.FileNumber}", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else _patients.Update(_editingPatient.Id, draft);
            PatientFormOverlay.Visibility = Visibility.Collapsed;
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تحقق من البيانات", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AppointmentFileNumberBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(AppointmentFileNumberBox.Text, out var number) && _patients.FindByFileNumber(number) is { } patient)
        {
            AppointmentPatientName.Text = patient.FullName;
            AppointmentNationalId.Text = patient.NationalId;
            AppointmentMobile.Text = patient.Mobile;
            AppointmentContact.Text = string.IsNullOrWhiteSpace(patient.SecondaryContact) ? "—" : patient.SecondaryContact;
            AppointmentMedical.Text = string.IsNullOrWhiteSpace(patient.BriefMedicalInfo) ? "—" : patient.BriefMedicalInfo;
        }
        else ClearAppointmentPatientCard("رقم الملف غير موجود");
    }

    private void ClearAppointmentPatientCard(string name)
    {
        AppointmentPatientName.Text = name;
        AppointmentNationalId.Text = AppointmentMobile.Text = AppointmentContact.Text = AppointmentMedical.Text = "—";
    }

    private Appointment SaveAppointmentFromForm()
    {
        if (!int.TryParse(AppointmentFileNumberBox.Text, out var fileNumber))
            throw new InvalidOperationException("أدخل رقم ملف صحيحاً.");
        if (AppointmentYearBox.SelectedItem is not int year || AppointmentMonthBox.SelectedItem is not int month ||
            AppointmentDayBox.SelectedItem is not int day || AppointmentHourBox.SelectedItem is not string hourText ||
            AppointmentMinuteBox.SelectedItem is not string minuteText)
            throw new InvalidOperationException("اختر التاريخ والوقت كاملاً.");
        var startsAt = new DateTime(year, month, day, int.Parse(hourText), int.Parse(minuteText), 0);
        return _appointments.Add(fileNumber, startsAt, AppointmentNotesBox.Text);
    }

    private void SaveAppointmentInline_Click(object sender, RoutedEventArgs e) => SaveAppointmentInline(false);
    private void SaveAndPrintAppointmentInline_Click(object sender, RoutedEventArgs e) => SaveAppointmentInline(true);

    private void SaveAppointmentInline(bool print)
    {
        try
        {
            var appointment = SaveAppointmentFromForm();
            AppointmentFormOverlay.Visibility = Visibility.Collapsed;
            RefreshAll();
            if (print) PrintAppointment(appointment);
            else MessageBox.Show("تم حفظ الموعد بنجاح.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تعذر حفظ الموعد", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CloseInlineForm_Click(object sender, RoutedEventArgs e)
    {
        PatientFormOverlay.Visibility = Visibility.Collapsed;
        AppointmentFormOverlay.Visibility = Visibility.Collapsed;
    }

    private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.Set("clinic_name", ClinicNameBox.Text);
        MessageBox.Show("تم حفظ الإعدادات وستبقى بعد إعادة تشغيل البرنامج.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _settings.CreateBackup();
            MessageBox.Show("تم إنشاء النسخة الاحتياطية بنجاح:\n" + path, "النسخ الاحتياطي", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تعذر إنشاء النسخة", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
