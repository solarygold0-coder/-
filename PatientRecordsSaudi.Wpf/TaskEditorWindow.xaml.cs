using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;
using FluentWindow = global::Wpf.Ui.Controls.FluentWindow;

namespace PatientRecordsSaudi.Desktop;

public partial class TaskEditorWindow : FluentWindow
{
    private readonly AppDatabase database;
    private readonly PatientTask? original;
    private Patient? selectedPatient;
    public PatientTask? Result { get; private set; }

    public TaskEditorWindow(AppDatabase database, PatientTask? task, long? initialFileNumber)
    {
        this.database = database;
        original = task;
        InitializeComponent();
        DataObject.AddPastingHandler(FileNumberBox, DigitsOnly_Paste);
        PriorityBox.ItemsSource = database.GetSettings().TaskPriorities;
        if (PriorityBox.Items.Count > 0) PriorityBox.SelectedIndex = 0;
        DuePicker.ConfigureYearRange(DateTime.Today.Year, DateTime.Today.Year + 10);
        DuePicker.Value = DateTime.Now.AddHours(1);
        HeaderText.Text = task is null ? "مهمة / تنبيه جديد" : "تعديل المهمة / التنبيه";
        if (task is not null) LoadTask(task);
        else if (initialFileNumber.HasValue) { FileNumberBox.Text = initialFileNumber.Value.ToString(); ResolvePatient(false); }
    }

    private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = e.Text.Any(c => !char.IsDigit(c));
    private static void DigitsOnly_Paste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText) || e.DataObject.GetData(DataFormats.UnicodeText) is not string value || value.Any(c => !char.IsDigit(c))) e.CancelCommand();
    }
    private void ResolvePatient_Click(object sender, RoutedEventArgs e) => ResolvePatient(true);
    private bool ResolvePatient(bool showError)
    {
        selectedPatient = null; PatientNameBox.Clear();
        if (!long.TryParse(SaudiValidation.NormalizeDigits(FileNumberBox.Text), out long number)) { if (showError) ShowError("أدخل رقم ملف صحيحًا بالأرقام فقط."); return false; }
        selectedPatient = database.FindByFileNumber(number, false);
        if (selectedPatient is null) { if (showError) ShowError("لا يوجد مراجع نشط بهذا الرقم."); return false; }
        PatientNameBox.Text = selectedPatient.FullName;
        return true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ResolvePatient(true)) return;
            if (string.IsNullOrWhiteSpace(TitleBox.Text)) throw new InvalidOperationException("أدخل اسم المهمة أو التنبيه.");
            DateTime due = DuePicker.Value;
            if (original is null && due < DateTime.Now.AddMinutes(-1)) throw new InvalidOperationException("لا يمكن إنشاء مهمة جديدة بوقت سابق.");
            PatientTask task = original ?? new PatientTask();
            task.PatientId = selectedPatient!.Id;
            task.FileNumber = selectedPatient.FileNumber;
            task.PatientName = selectedPatient.FullName;
            task.Title = TitleBox.Text.Trim();
            task.DueAt = due;
            task.Priority = PriorityBox.SelectedItem?.ToString() ?? PriorityBox.Text;
            task.IsCompleted = CompletionBox.SelectedIndex == 1;
            task.Notes = NotesBox.Text.Trim();
            Result = task;
            DialogResult = true;
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void LoadTask(PatientTask task)
    {
        FileNumberBox.Text = task.FileNumber.ToString(); ResolvePatient(false);
        TitleBox.Text = task.Title; DuePicker.Value = task.DueAt; PriorityBox.SelectedItem = task.Priority;
        CompletionBox.SelectedIndex = task.IsCompleted ? 1 : 0; NotesBox.Text = task.Notes;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private static void ShowError(string text) => MessageBox.Show(text, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
}
