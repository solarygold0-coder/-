using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;
using FluentWindow = global::Wpf.Ui.Controls.FluentWindow;

namespace PatientRecordsSaudi.Desktop;

public partial class AppointmentEditorWindow : FluentWindow
{
    private readonly AppDatabase database;
    private readonly Appointment? original;
    private Patient? selectedPatient;
    public Appointment? Result { get; private set; }

    public AppointmentEditorWindow(AppDatabase database, Appointment? appointment, long? initialFileNumber)
    {
        this.database = database;
        original = appointment;
        InitializeComponent();
        AppSettings settings = database.GetSettings();
        VisitTypeBox.ItemsSource = settings.VisitTypes;
        StatusBox.ItemsSource = settings.AppointmentStatuses;
        if (VisitTypeBox.Items.Count > 0) VisitTypeBox.SelectedIndex = 0;
        if (StatusBox.Items.Count > 0) StatusBox.SelectedIndex = 0;
        StartsAtPicker.ConfigureYearRange(DateTime.Today.Year, DateTime.Today.Year + 10);
        HeaderText.Text = appointment is null ? "موعد جديد" : "تعديل الموعد";

        if (appointment is null)
        {
            int minutes = settings.DefaultAppointmentMinutes;
            SelectCombo(DurationBox, minutes.ToString());
            StartsAtPicker.Value = database.GetNextAvailableAppointmentTime(minutes);
            if (initialFileNumber.HasValue) { FileNumberBox.Text = initialFileNumber.Value.ToString(); ResolvePatient(false); }
        }
        else LoadAppointment(appointment);
    }

    private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = e.Text.Any(c => !char.IsDigit(c));
    private static string ComboText(ComboBox combo) => combo.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? "" : combo.SelectedItem?.ToString() ?? combo.Text;
    private static void SelectCombo(ComboBox combo, string value)
    {
        foreach (object item in combo.Items)
            if ((item is ComboBoxItem box ? box.Content?.ToString() : item.ToString()) == value) { combo.SelectedItem = item; return; }
        combo.Text = value;
    }

    private void ResolvePatient_Click(object sender, RoutedEventArgs e) => ResolvePatient(true);
    private bool ResolvePatient(bool showError)
    {
        selectedPatient = null;
        PatientNameBox.Clear(); NationalIdBox.Clear(); MobileBox.Clear();
        if (!long.TryParse(SaudiValidation.NormalizeDigits(FileNumberBox.Text), out long number)) { if (showError) ShowError("أدخل رقم ملف صحيحًا بالأرقام فقط."); return false; }
        selectedPatient = database.FindByFileNumber(number, false);
        if (selectedPatient is null) { if (showError) ShowError("لا يوجد مراجع نشط بهذا الرقم. تحقق من رقم الملف."); return false; }
        PatientNameBox.Text = selectedPatient.FullName;
        NationalIdBox.Text = selectedPatient.NationalId;
        MobileBox.Text = selectedPatient.Mobile;
        return true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ResolvePatient(true)) return;
            if (string.IsNullOrWhiteSpace(TitleBox.Text)) throw new InvalidOperationException("أدخل عنوان الموعد.");
            DateTime startsAt = StartsAtPicker.Value;
            if (!SaudiValidation.IsOfficialWorkingDay(startsAt)) throw new InvalidOperationException("لا تُقبل المواعيد يوم الجمعة أو السبت. اختر يومًا من الأحد إلى الخميس.");
            if (!int.TryParse(ComboText(DurationBox), out int minutes) || minutes < 5) throw new InvalidOperationException("مدة الموعد غير صحيحة.");
            Appointment appointment = original ?? new Appointment();
            appointment.PatientId = selectedPatient!.Id;
            appointment.FileNumber = selectedPatient.FileNumber;
            appointment.PatientName = selectedPatient.FullName;
            appointment.Title = TitleBox.Text.Trim();
            appointment.VisitType = VisitTypeBox.SelectedItem?.ToString() ?? VisitTypeBox.Text;
            appointment.StartsAt = startsAt;
            appointment.DurationMinutes = minutes;
            appointment.Status = StatusBox.SelectedItem?.ToString() ?? StatusBox.Text;
            appointment.Notes = NotesBox.Text.Trim();
            database.ValidateAppointmentAvailability(appointment);
            Result = appointment;
            DialogResult = true;
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void LoadAppointment(Appointment appointment)
    {
        FileNumberBox.Text = appointment.FileNumber.ToString();
        ResolvePatient(false);
        TitleBox.Text = appointment.Title;
        VisitTypeBox.SelectedItem = appointment.VisitType;
        StartsAtPicker.Value = appointment.StartsAt;
        SelectCombo(DurationBox, appointment.DurationMinutes.ToString());
        StatusBox.SelectedItem = appointment.Status;
        NotesBox.Text = appointment.Notes;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private static void ShowError(string text) => MessageBox.Show(text, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
}
