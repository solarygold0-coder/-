using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PatientRecordsSaudi.Models;
using FluentWindow = global::Wpf.Ui.Controls.FluentWindow;

namespace PatientRecordsSaudi.Desktop;

public partial class ReminderWindow : FluentWindow
{
    private readonly Action<Guid> openPatient;

    public ReminderWindow(IReadOnlyCollection<Appointment> appointments, IReadOnlyCollection<PatientTask> tasks, Action<Guid> openPatient)
    {
        this.openPatient = openPatient;
        InitializeComponent();
        AppointmentsGrid.ItemsSource = new ObservableCollection<Appointment>(appointments);
        TasksGrid.ItemsSource = new ObservableCollection<PatientTask>(tasks);
        AppointmentsEmpty.Visibility = appointments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TasksEmpty.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenPatient_Click(object sender, RoutedEventArgs e)
    {
        Guid patientId = (sender as Button)?.DataContext switch
        {
            Appointment appointment => appointment.PatientId,
            PatientTask task => task.PatientId,
            _ => Guid.Empty
        };
        if (patientId == Guid.Empty) return;
        Close();
        openPatient(patientId);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
