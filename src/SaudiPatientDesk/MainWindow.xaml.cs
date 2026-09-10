using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SaudiPatientDesk.Domain;
using SaudiPatientDesk.Services;

namespace SaudiPatientDesk;

public partial class MainWindow : Window
{
    private readonly PatientService _patients = new();
    private readonly AppointmentService _appointments = new();

    public MainWindow()
    {
        InitializeComponent();
        RefreshAll();
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
        DashboardPanel.Visibility = key == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        PatientsPanel.Visibility = key == "patients" ? Visibility.Visible : Visibility.Collapsed;
        AppointmentsPanel.Visibility = key == "appointments" ? Visibility.Visible : Visibility.Collapsed;
        SearchPanel.Visibility = key == "search" ? Visibility.Visible : Visibility.Collapsed;
        (PageTitle.Text, PageSubtitle.Text) = key switch
        {
            "patients" => ("المرضى", "إضافة السجلات وتعديلها والبحث فيها"),
            "appointments" => ("المواعيد", "المواعيد القادمة ومنع التعارض تلقائياً"),
            "search" => ("البحث الشامل", "الوصول السريع إلى ملف المريض"),
            _ => ("لوحة التحكم", "ملخص العمل والمواعيد القادمة")
        };
    }

    private void AddPatient_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PatientEditorWindow(_patients) { Owner = this };
        if (dialog.ShowDialog() == true) RefreshAll();
    }

    private void AddAppointment_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AppointmentEditorWindow(_patients, _appointments) { Owner = this };
        if (dialog.ShowDialog() == true) RefreshAll();
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
        var dialog = new PatientEditorWindow(_patients, patient) { Owner = this };
        if (dialog.ShowDialog() == true) RefreshAll();
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
            EditSelected(_patients.FindByFileNumber(item.FileNumber));
    }
}
