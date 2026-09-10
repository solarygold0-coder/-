using System.Windows;
using System.Windows.Controls;
using SaudiPatientDesk.Services;

namespace SaudiPatientDesk;

public partial class AppointmentEditorWindow : Window
{
    private readonly PatientService _patients;
    private readonly AppointmentService _appointments;

    public AppointmentEditorWindow(PatientService patients, AppointmentService appointments)
    {
        InitializeComponent();
        _patients = patients;
        _appointments = appointments;
        var tomorrow = DateTime.Today.AddDays(1);
        DayBox.ItemsSource = Enumerable.Range(1, 31);
        MonthBox.ItemsSource = Enumerable.Range(1, 12);
        YearBox.ItemsSource = Enumerable.Range(DateTime.Today.Year, 5);
        HourBox.ItemsSource = Enumerable.Range(7, 14).Select(x => x.ToString("00"));
        MinuteBox.ItemsSource = new[] { "00", "15", "30", "45" };
        DayBox.SelectedItem = tomorrow.Day;
        MonthBox.SelectedItem = tomorrow.Month;
        YearBox.SelectedItem = tomorrow.Year;
        HourBox.SelectedItem = "09";
        MinuteBox.SelectedItem = "00";
    }

    private void FileNumberBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(FileNumberBox.Text, out var number) && _patients.FindByFileNumber(number) is { } patient)
            PatientNameText.Text = patient.FullName;
        else PatientNameText.Text = "رقم الملف غير موجود";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(FileNumberBox.Text, out var fileNumber)) throw new InvalidOperationException("أدخل رقم ملف صحيحاً.");
            if (YearBox.SelectedItem is not int year || MonthBox.SelectedItem is not int month || DayBox.SelectedItem is not int day ||
                HourBox.SelectedItem is not string hourText || MinuteBox.SelectedItem is not string minuteText)
                throw new InvalidOperationException("اختر التاريخ والوقت كاملاً.");
            var startsAt = new DateTime(year, month, day, int.Parse(hourText), int.Parse(minuteText), 0);
            _appointments.Add(fileNumber, startsAt, NotesBox.Text);
            MessageBox.Show("تم حفظ الموعد بنجاح.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تعذر حفظ الموعد", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
