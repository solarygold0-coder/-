using System.Collections.ObjectModel;
using System.Windows;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;
using FluentWindow = global::Wpf.Ui.Controls.FluentWindow;

namespace PatientRecordsSaudi.Desktop;

public partial class RecycleBinWindow : FluentWindow
{
    private readonly AppDatabase database;
    public RecycleBinWindow(AppDatabase database) { this.database = database; InitializeComponent(); LoadData(); }
    private void LoadData() { AppointmentsGrid.ItemsSource = new ObservableCollection<Appointment>(database.GetDeletedAppointments()); TasksGrid.ItemsSource = new ObservableCollection<PatientTask>(database.GetDeletedTasks()); }
    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (RecycleTabs.SelectedIndex == 0)
            {
                if (AppointmentsGrid.SelectedItem is not Appointment item) { ShowError("اختر موعدًا محذوفًا."); return; }
                database.RestoreAppointment(item.Id);
            }
            else
            {
                if (TasksGrid.SelectedItem is not PatientTask item) { ShowError("اختر مهمة محذوفة."); return; }
                database.RestoreTask(item.Id);
            }
            LoadData();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private static void ShowError(string text) => MessageBox.Show(text, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
}
