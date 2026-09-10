using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using SaudiPatientDesk.Domain;
using SaudiPatientDesk.Services;

namespace SaudiPatientDesk;

public partial class PatientEditorWindow : Window
{
    private readonly PatientService _service;
    private readonly Patient? _patient;

    public PatientEditorWindow(PatientService service, Patient? patient = null)
    {
        InitializeComponent();
        _service = service;
        _patient = patient;
        if (patient is null) return;
        Heading.Text = "تعديل ملف المريض";
        FileNumberText.Text = $"رقم الملف: {patient.FileNumber}";
        SaveButton.Content = "حفظ التعديلات";
        FullNameBox.Text = patient.FullName;
        NationalIdBox.Text = patient.NationalId;
        MobileBox.Text = patient.Mobile;
        SecondaryBox.Text = patient.SecondaryContact;
        MedicalBox.Text = patient.BriefMedicalInfo;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var draft = new PatientDraft(FullNameBox.Text, NationalIdBox.Text, MobileBox.Text, SecondaryBox.Text, MedicalBox.Text);
            if (_patient is null)
            {
                var created = _service.Add(draft);
                MessageBox.Show($"تمت إضافة المريض بنجاح.\nرقم الملف: {created.FileNumber}", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else _service.Update(_patient.Id, draft);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تحقق من البيانات", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
}
