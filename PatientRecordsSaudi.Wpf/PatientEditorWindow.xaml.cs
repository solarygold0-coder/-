using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;
using FluentWindow = global::Wpf.Ui.Controls.FluentWindow;

namespace PatientRecordsSaudi.Desktop;

public partial class PatientEditorWindow : FluentWindow
{
    private readonly AppDatabase database;
    private readonly Patient? original;
    private readonly bool readOnly;
    public Patient? Result { get; private set; }

    public PatientEditorWindow(AppDatabase database, Patient? patient, bool readOnly)
    {
        this.database = database;
        original = patient;
        this.readOnly = readOnly;
        InitializeComponent();

        AppSettings settings = database.GetSettings();
        GenderBox.ItemsSource = settings.GenderOptions;
        BloodTypeBox.ItemsSource = settings.BloodTypes;
        BirthDatePicker.ConfigureYearRange(1900, DateTime.Today.Year);
        HeaderText.Text = patient is null ? "إضافة مراجع جديد" : "ملف المراجع رقم " + patient.FileNumber;
        HeaderSubText.Text = patient is null ? "سيُنشأ رقم الملف تلقائيًا عند الحفظ" : (patient.IsArchived ? "ملف مؤرشف — قراءة فقط" : "بيانات المراجع ومرفقاته المشفرة");

        if (patient is null)
        {
            FileNumberBox.Text = "يُنشأ تلقائيًا";
            NationalityBox.Text = "سعودي";
            BirthDatePicker.Value = DateTime.Today.AddYears(-30);
            if (GenderBox.Items.Count > 0) GenderBox.SelectedIndex = 0;
            if (BloodTypeBox.Items.Count > 0) BloodTypeBox.SelectedIndex = 0;
        }
        else LoadPatient(patient);

        if (readOnly)
        {
            PersonalTab.IsEnabled = ContactTab.IsEnabled = MedicalTab.IsEnabled = false;
            SaveButton.Visibility = Visibility.Collapsed;
            AddAttachmentButton.IsEnabled = DeleteAttachmentButton.IsEnabled = RestoreAttachmentButton.IsEnabled = false;
        }
        if (patient is null) AddAttachmentButton.IsEnabled = DeleteAttachmentButton.IsEnabled = RestoreAttachmentButton.IsEnabled = false;
        LoadAttachments();
    }

    private static string ComboText(ComboBox combo) => combo.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? "" : combo.SelectedItem?.ToString() ?? combo.Text;
    private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = e.Text.Any(c => !char.IsDigit(c));
    private void Phone_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = e.Text.Any(c => !char.IsDigit(c) && c != '+' && c != '-' && c != ' ');

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string identityType = ComboText(IdentityTypeBox);
            if (!SaudiValidation.ValidateSaudiIdentity(NationalIdBox.Text, identityType, out string identityError)) throw new InvalidOperationException(identityError);
            if (string.IsNullOrWhiteSpace(FullNameBox.Text) || FullNameBox.Text.Trim().Length < 4 || !FullNameBox.Text.Any(char.IsLetter)) throw new InvalidOperationException("أدخل الاسم الكامل بصورة صحيحة ولا تستخدم أرقامًا فقط.");
            DateTime birth = BirthDatePicker.Value.Date;
            if (birth > DateTime.Today) throw new InvalidOperationException("تاريخ الميلاد لا يمكن أن يكون في المستقبل.");
            if (!SaudiValidation.ValidateSaudiMobile(MobileBox.Text, true, out string mobileError)) throw new InvalidOperationException(mobileError);
            if (!SaudiValidation.ValidateSaudiMobile(AlternatePhoneBox.Text, false, out string alternateError)) throw new InvalidOperationException("الهاتف البديل: " + alternateError);
            if (!SaudiValidation.ValidateSaudiMobile(EmergencyPhoneBox.Text, false, out string emergencyError)) throw new InvalidOperationException("جوال الطوارئ: " + emergencyError);
            if (string.IsNullOrWhiteSpace(CityBox.Text)) throw new InvalidOperationException("أدخل مدينة المراجع.");

            Patient patient = original ?? new Patient();
            patient.IdentityType = identityType;
            patient.NationalId = SaudiValidation.NormalizeDigits(NationalIdBox.Text);
            patient.FullName = FullNameBox.Text.Trim();
            patient.Gender = ComboText(GenderBox);
            patient.DateOfBirth = birth;
            patient.Nationality = NationalityBox.Text.Trim();
            patient.Mobile = SaudiValidation.NormalizeSaudiMobile(MobileBox.Text);
            patient.AlternatePhone = SaudiValidation.NormalizeSaudiMobile(AlternatePhoneBox.Text);
            patient.City = CityBox.Text.Trim();
            patient.Address = AddressBox.Text.Trim();
            patient.EmergencyContact = EmergencyNameBox.Text.Trim();
            patient.EmergencyPhone = SaudiValidation.NormalizeSaudiMobile(EmergencyPhoneBox.Text);
            patient.BloodType = ComboText(BloodTypeBox);
            patient.Allergies = AllergiesBox.Text.Trim();
            patient.ChronicConditions = ChronicBox.Text.Trim();
            patient.Notes = NotesBox.Text.Trim();
            Result = patient;
            DialogResult = true;
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void LoadPatient(Patient patient)
    {
        FileNumberBox.Text = patient.FileNumber.ToString();
        SelectCombo(IdentityTypeBox, patient.IdentityType);
        NationalIdBox.Text = patient.NationalId;
        FullNameBox.Text = patient.FullName;
        GenderBox.SelectedItem = patient.Gender;
        if (patient.DateOfBirth.HasValue) BirthDatePicker.Value = patient.DateOfBirth.Value;
        NationalityBox.Text = patient.Nationality;
        MobileBox.Text = patient.Mobile;
        AlternatePhoneBox.Text = patient.AlternatePhone;
        CityBox.Text = patient.City;
        AddressBox.Text = patient.Address;
        EmergencyNameBox.Text = patient.EmergencyContact;
        EmergencyPhoneBox.Text = patient.EmergencyPhone;
        BloodTypeBox.SelectedItem = patient.BloodType;
        AllergiesBox.Text = patient.Allergies;
        ChronicBox.Text = patient.ChronicConditions;
        NotesBox.Text = patient.Notes;
    }

    private static void SelectCombo(ComboBox combo, string value)
    {
        foreach (object item in combo.Items)
            if (item is ComboBoxItem box && string.Equals(box.Content?.ToString(), value, StringComparison.Ordinal)) { combo.SelectedItem = item; return; }
        combo.Text = value;
    }

    private void LoadAttachments()
    {
        AttachmentsGrid.ItemsSource = new ObservableCollection<PatientAttachment>(original is null ? new List<PatientAttachment>() : database.GetAttachments(original.Id, ShowDeletedAttachmentsCheck.IsChecked == true));
    }

    private PatientAttachment? SelectedAttachment() => AttachmentsGrid.SelectedItem as PatientAttachment;
    private void ShowDeletedAttachments_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) LoadAttachments(); }
    private void AttachmentsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenAttachment_Click(sender, e);

    private void AddAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (original is null) { ShowError("احفظ ملف المراجع أولًا ثم افتحه لإضافة المرفقات."); return; }
        var dialog = new OpenFileDialog { Filter = "الملفات المسموحة|*.pdf;*.jpg;*.jpeg;*.png;*.docx", Multiselect = false, Title = "اختر مرفق المراجع" };
        if (dialog.ShowDialog(this) != true) return;
        try { database.AddAttachment(original.Id, dialog.FileName, ComboText(AttachmentCategoryBox)); LoadAttachments(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void OpenAttachment_Click(object sender, RoutedEventArgs e)
    {
        PatientAttachment? attachment = SelectedAttachment();
        if (attachment is null) { ShowError("اختر مرفقًا أولًا."); return; }
        if (attachment.IsDeleted) { ShowError("استعد المرفق أولًا قبل فتحه."); return; }
        try
        {
            string path = database.ExportAttachmentToTemporaryFile(attachment.Id);
            Process? viewer = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            AppDatabase.ScheduleTemporaryAttachmentCleanup(path, viewer!);
        }
        catch (Exception ex) { ShowError("تعذر فتح المرفق: " + ex.Message); }
    }

    private void DeleteAttachment_Click(object sender, RoutedEventArgs e)
    {
        PatientAttachment? attachment = SelectedAttachment();
        if (attachment is null || attachment.IsDeleted) return;
        if (Confirm("نقل المرفق «" + attachment.OriginalName + "» إلى المحذوفات؟")) { database.DeleteAttachment(attachment.Id); LoadAttachments(); }
    }

    private void RestoreAttachment_Click(object sender, RoutedEventArgs e)
    {
        PatientAttachment? attachment = SelectedAttachment();
        if (attachment is null || !attachment.IsDeleted) return;
        try { database.RestoreAttachment(attachment.Id); LoadAttachments(); } catch (Exception ex) { ShowError(ex.Message); }
    }

    private static bool Confirm(string text) => MessageBox.Show(text, "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.RtlReading) == MessageBoxResult.Yes;
    private static void ShowError(string text) => MessageBox.Show(text, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
}
