using System.Collections.ObjectModel;
using System.Windows;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;
using Wpf.Ui.Controls;

namespace PatientRecordsSaudi.Wpf;

public partial class ClosureDatesWindow : FluentWindow
{
    private readonly AppDatabase database;
    public ClosureDatesWindow(AppDatabase database)
    {
        this.database = database; InitializeComponent();
        DatePicker.ConfigureYearRange(DateTime.Today.Year, DateTime.Today.Year + 10); DatePicker.Value = DateTime.Today;
        LoadData();
    }
    private void LoadData() => ClosuresGrid.ItemsSource = new ObservableCollection<ClosureDate>(database.GetClosures());
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        try { database.AddClosure(DatePicker.Value.Date, ReasonBox.Text); ReasonBox.Clear(); LoadData(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ClosuresGrid.SelectedItem is not ClosureDate item) { ShowError("اختر يوم إغلاق أولًا."); return; }
        if (MessageBox.Show("حذف يوم الإغلاق المحدد؟", "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.RtlReading) == MessageBoxResult.Yes) { database.DeleteClosure(item.Id); LoadData(); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private static void ShowError(string text) => MessageBox.Show(text, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
}
