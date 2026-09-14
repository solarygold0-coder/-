using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SaudiPatientDesk.Data;
using SaudiPatientDesk.Domain;
using SaudiPatientDesk.Services;

namespace SaudiPatientDesk;

public partial class MainWindow : Window
{
    private readonly PatientService _patients = new();
    private readonly AppointmentService _appointments = new();
    private readonly SettingsService _settings = new();
    private readonly AppointmentPrintService _printer = new();
    private readonly SmartAssistService _smart = new();
    private readonly StaffService _staff = new();
    private readonly ClinicService _clinics = new();
    private Patient? _editingPatient;
    private Appointment? _editingAppointment;
    private bool _initializingAppointmentDate;
    private DateTime _timelineDay = DateTime.Today;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"{AppInfo.Version} • ميلادي فقط";
        ShowPanel("dashboard");
        Loc.ApplyToWindow(this);
        ClinicNameBox.Text = _settings.Get("clinic_name", "");
        SelectLanguageCombo();
        ApplyShellLanguage();
        LoadClinicLogo();
        DataLocationBox.Text = _settings.DataLocation;
        LoadWorkingHoursControls();
        RefreshBreakHint();
        InitializeAppointmentSelectors();
        RefreshClosures();
        RefreshAll();
    }

    private void InitializeAppointmentSelectors(DateTime? selected = null)
    {
        _initializingAppointmentDate = true;
        var hours = ClinicHours.Load(_settings);
        var (doctorId, specialistId) = SelectedClinicianIds();
        var value = selected ?? (_smart.SuggestNextOpenSlot(DateTime.Now, 14, doctorId, specialistId, _editingAppointment?.Id)
            ?? DateTime.Today.AddDays(1).Add(hours.Start));

        var firstYear = Math.Min(DateTime.Today.Year, value.Year);
        AppointmentMonthBox.ItemsSource = Enumerable.Range(1, 12);
        AppointmentYearBox.ItemsSource = Enumerable.Range(firstYear, 21);

        AppointmentMonthBox.SelectedItem = value.Month;
        AppointmentYearBox.SelectedItem = value.Year;
        UpdateAppointmentDays(value.Day);

        var (h12, period) = TimeDisplay.FromHour24(value.Hour);
        AppointmentPeriodBox.ItemsSource = null;
        AppointmentPeriodBox.Items.Clear();
        AppointmentPeriodBox.ItemsSource = TimeDisplay.PeriodOptions;
        AppointmentHourBox.ItemsSource = TimeDisplay.Hour12Options;
        AppointmentMinuteBox.ItemsSource = TimeDisplay.MinuteOptions;
        AppointmentPeriodBox.SelectedItem = period;
        AppointmentHourBox.SelectedItem = h12.ToString();
        AppointmentMinuteBox.SelectedItem = value.Minute.ToString("00");

        _initializingAppointmentDate = false;
        UpdateSlotStatus();
    }

    private void AppointmentDatePart_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializingAppointmentDate)
        {
            UpdateAppointmentDays();
            UpdateSlotStatus();
        }
    }

    private void AppointmentTime_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingAppointmentDate) return;
        UpdateSlotStatus();
    }

    private string SelectedAppointmentPeriod() => AppointmentPeriodBox.SelectedItem?.ToString() ?? "ص";

    private void UpdateAppointmentDays(int? preferredDay = null)
    {
        if (AppointmentYearBox.SelectedItem is not int year || AppointmentMonthBox.SelectedItem is not int month)
            return;
        var day = preferredDay ?? (AppointmentDayBox.SelectedItem as int? ?? 1);
        var count = DateTime.DaysInMonth(year, month);
        AppointmentDayBox.ItemsSource = Enumerable.Range(1, count);
        AppointmentDayBox.SelectedItem = Math.Min(day, count);
        if (!_initializingAppointmentDate)
            UpdateSlotStatus();
    }

    private void RefreshAll()
    {
        var snapshot = _appointments.Snapshot();
        try { SmartInsightsBox.Text = string.Join("\n", _smart.DashboardInsights()); } catch (Exception ex) { SmartInsightsBox.Text = ex.Message; }
        PatientsCount.Text = snapshot.ActivePatients.ToString("N0");
        TodayCount.Text = snapshot.TodayAppointments.ToString("N0");
        AlertsCount.Text = snapshot.UpcomingAlerts.ToString("N0");
        InactiveCount.Text = snapshot.InactiveTenYears.ToString("N0");
        DashboardAppointmentsGrid.ItemsSource = _appointments.Upcoming();
        ApplyAppointmentFilter();
        PatientsGrid.ItemsSource = _patients.Search(PatientFilter.Text);
        RefreshTimelineAndToday();
        LoadStaffCombos();
        LoadAudit();
    }

    private void LoadAudit()
    {
        if (AuditGrid is null) return;
        AuditGrid.ItemsSource = Database.RecentLog().Select(r => new { r.At, r.Action, r.Detail }).ToList();
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
        PatientFormSubtitle.Text = "رقم الملف بالشكل A-0001 — توليد تلقائي أو إدخال يدوي";
        SavePatientButton.Content = "إضافة المريض";
        PatientFileNumberBox.Text = _patients.GenerateFileNumber();
        PatientFullNameBox.Clear();
        PatientNationalIdBox.Clear();
        PatientMobileBox.Clear();
        PatientGenderBox.SelectedIndex = 0;
        PatientResidencyBox.Clear();
        PatientBloodTypeBox.SelectedIndex = 0;
        PatientSecondaryBox.Clear();
        PatientChronicBox.Clear();
        PatientMedicationsBox.Clear();
        PatientAllergiesBox.Clear();
        PatientMedicalBox.Clear();
        PatientFormOverlay.Visibility = Visibility.Visible;
        PatientFullNameBox.Focus();
    }

    private void AddAppointment_Click(object sender, RoutedEventArgs e)
    {
        _editingAppointment = null;
        AppointmentFormTitle.Text = "إضافة موعد";
        AppointmentFormSubtitle.Text = "أدخل رقم الملف وستظهر بيانات المراجع كاملة";
        SaveAppointmentButton.Content = "حفظ الموعد";
        AppointmentFileNumberBox.Clear();
        AppointmentNotesBox.Clear();
        AppointmentDoctorBox.SelectedIndex = -1;
        AppointmentSpecialistBox.SelectedIndex = -1;
        AppointmentStaffBox.SelectedIndex = -1;
        AppointmentClinicBox.SelectedIndex = AppointmentClinicBox.Items.Count > 0 ? 0 : -1;
        AppointmentRenewedBox.IsChecked = false;
        InitializeAppointmentSelectors();
        ClearAppointmentPatientCard("لم يتم اختيار مراجع");
        AppointmentFormOverlay.Visibility = Visibility.Visible;
        AppointmentFileNumberBox.Focus();
    }

    private void EditAppointment_Click(object sender, RoutedEventArgs e) =>
        EditSelectedAppointment(AppointmentsGrid.SelectedItem as Appointment);

    private void EditSelectedAppointment(Appointment? appointment)
    {
        if (appointment is null)
        {
            MessageBox.Show("حدد موعداً من الجدول أولاً.", "تعديل الموعد", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _editingAppointment = appointment;
        AppointmentFormTitle.Text = "تعديل الموعد";
        AppointmentFormSubtitle.Text = $"رقم الموعد: {appointment.Id} • التاريخ ميلادي";
        SaveAppointmentButton.Content = "حفظ التعديلات";
        AppointmentFileNumberBox.Text = appointment.FileNumber;
        if (appointment.StaffId is long sid) AppointmentStaffBox.SelectedValue = sid;
        AppointmentDoctorBox.SelectedValue = appointment.DoctorId;
        AppointmentSpecialistBox.SelectedValue = appointment.SpecialistId;
        AppointmentClinicBox.SelectedValue = appointment.ClinicId;
        AppointmentRenewedBox.IsChecked = appointment.Kind == "renewed";
        AppointmentNotesBox.Text = appointment.Notes;
        InitializeAppointmentSelectors(appointment.StartsAt);
        AppointmentFormOverlay.Visibility = Visibility.Visible;
        AppointmentFileNumberBox.Focus();
    }

    private void CancelAppointment_Click(object sender, RoutedEventArgs e)
    {
        if (AppointmentsGrid.SelectedItem is not Appointment appointment)
        {
            MessageBox.Show("حدد موعداً من الجدول أولاً.", "إلغاء الموعد", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (appointment.Status == "cancelled")
        {
            MessageBox.Show("هذا الموعد ملغي مسبقاً.", "إلغاء الموعد", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"هل تريد إلغاء موعد {appointment.PatientName}؟\nسيبقى الموعد محفوظاً في السجل.",
            "تأكيد إلغاء الموعد", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _appointments.Cancel(appointment.Id);
        RefreshAll();
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
        PatientFileNumberBox.Text = patient.FileNumber;
        PatientFullNameBox.Text = patient.FullName;
        PatientNationalIdBox.Text = patient.NationalId;
        PatientMobileBox.Text = patient.Mobile;
        PatientGenderBox.SelectedIndex = patient.Gender == "female" ? 1 : 0;
        PatientResidencyBox.Text = patient.ResidencyNumber ?? "";
        SetBloodTypeCombo(patient.BloodType);
        PatientSecondaryBox.Text = patient.SecondaryContact ?? "";
        PatientChronicBox.Text = patient.ChronicDiseases ?? "";
        PatientMedicationsBox.Text = patient.CurrentMedications ?? "";
        PatientAllergiesBox.Text = patient.DrugAllergies ?? "";
        PatientMedicalBox.Text = patient.BriefMedicalInfo ?? "";
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
            EditSelectedAppointment(item);
    }

    private void PrintSelectedAppointment_Click(object sender, RoutedEventArgs e)
    {
        if (AppointmentsGrid.SelectedItem is not Appointment item)
        {
            MessageBox.Show("حدد موعداً من الجدول أولاً.", "طباعة الموعد", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        TryPrintAppointment(item);
    }

    private void PrintAppointment(Appointment appointment)
    {
        var patient = _patients.FindByFileNumber(appointment.FileNumber);
        if (patient is null) return;
        _printer.Print(appointment, patient, _settings.Get("clinic_name", "نظام سجلات المرضى"));
    }

    private void TryPrintAppointment(Appointment appointment)
    {
        try
        {
            PrintAppointment(appointment);
        }
        catch (Exception ex)
        {
            MessageBox.Show("تعذرت طباعة الموعد.\n" + ex.Message,
                "الطباعة", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SavePatientInline_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var draft = new PatientDraft(
                PatientFileNumberBox.Text,
                PatientFullNameBox.Text,
                PatientNationalIdBox.Text,
                PatientMobileBox.Text,
                PatientSecondaryBox.Text,
                PatientMedicalBox.Text,
                GetSelectedGender(),
                PatientResidencyBox.Text,
                GetSelectedBloodType(),
                PatientChronicBox.Text,
                PatientMedicationsBox.Text,
                PatientAllergiesBox.Text);
            if (_editingPatient is null)
            {
                var created = _patients.Add(draft);
                MessageBox.Show($"تمت إضافة المريض. رقم الملف: {created.FileNumber}", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
                try
                {
                    _printer.PrintFileCard(created, _settings.Get("clinic_name", "نظام سجلات المرضى"));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("تم حفظ المريض، لكن تعذرت طباعة بطاقة المراجع.\n" + ex.Message,
                        "الطباعة", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
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

    private void GenerateFileNumber_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PatientFileNumberBox.Text = _patients.GenerateFileNumber();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تعذر التوليد", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }


    private string GetSelectedGender()
    {
        if (PatientGenderBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            return tag;
        return "male";
    }

    private string? GetSelectedBloodType()
    {
        if (PatientBloodTypeBox.SelectedItem is ComboBoxItem item)
        {
            var text = item.Content?.ToString();
            if (string.IsNullOrWhiteSpace(text) || text == "—") return null;
            return text;
        }
        return null;
    }

    private void SetBloodTypeCombo(string? bloodType)
    {
        PatientBloodTypeBox.SelectedIndex = 0;
        if (string.IsNullOrWhiteSpace(bloodType)) return;
        for (int i = 0; i < PatientBloodTypeBox.Items.Count; i++)
        {
            if (PatientBloodTypeBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Content?.ToString(), bloodType, StringComparison.OrdinalIgnoreCase))
            {
                PatientBloodTypeBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void FileNumber_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Allow A-Z, a-z, digits, and hyphen only
        e.Handled = !System.Text.RegularExpressions.Regex.IsMatch(e.Text, "^[A-Za-z0-9-]+$");
    }

    private void AppointmentFileNumberBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var code = AppointmentFileNumberBox.Text.Trim();
        if (!string.IsNullOrEmpty(code) && _patients.FindByFileNumber(code) is { } patient)
        {
            AppointmentPatientName.Text = patient.FullName;
            AppointmentNationalId.Text = patient.NationalId;
            AppointmentMobile.Text = patient.Mobile;
            AppointmentContact.Text = string.IsNullOrWhiteSpace(patient.SecondaryContact) ? "—" : patient.SecondaryContact;
            AppointmentGender.Text = patient.GenderDisplay;
            AppointmentResidency.Text = DisplayValue(patient.ResidencyNumber);
            AppointmentBloodType.Text = DisplayValue(patient.BloodType);
            AppointmentChronic.Text = DisplayValue(patient.ChronicDiseases);
            AppointmentMedications.Text = DisplayValue(patient.CurrentMedications);
            AppointmentAllergies.Text = DisplayValue(patient.DrugAllergies);
            AppointmentMedical.Text = DisplayValue(patient.BriefMedicalInfo);
            ApplySafetyBanner(patient);
        }
        else
        {
            ClearAppointmentPatientCard(string.IsNullOrWhiteSpace(code) ? "أدخل رقم الملف" : "رقم الملف غير موجود");
            ClearSafetyBanner();
        }
    }

    private void ApplySafetyBanner(Patient patient)
    {
        var flags = _smart.GetSafetyFlags(patient);
        var hint = _smart.GetNoShowHint(patient.Id);
        var show = flags.HasAnyAlert || hint is not null;
        SafetyBanner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SafetyBannerText.Text = flags.HasAnyAlert ? flags.ArabicBannerText : "";
        if (hint is not null)
        {
            NoShowHintText.Text = hint.MessageAr;
            NoShowHintText.Visibility = Visibility.Visible;
            if (!flags.HasAnyAlert)
                SafetyBannerText.Text = hint.MessageAr;
        }
        else
        {
            NoShowHintText.Text = "";
            NoShowHintText.Visibility = Visibility.Collapsed;
        }
    }

    private void ClearSafetyBanner()
    {
        SafetyBanner.Visibility = Visibility.Collapsed;
        SafetyBannerText.Text = "";
        NoShowHintText.Text = "";
        NoShowHintText.Visibility = Visibility.Collapsed;
    }

    private void SuggestNextSlot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var from = DateTime.Now.AddMinutes(15);
            if (AppointmentYearBox.SelectedItem is int year &&
                AppointmentMonthBox.SelectedItem is int month &&
                AppointmentDayBox.SelectedItem is int day &&
                AppointmentHourBox.SelectedItem is string hs && int.TryParse(hs, out var h12) &&
                AppointmentMinuteBox.SelectedItem is string ms && int.TryParse(ms, out var minute))
            {
                var period = SelectedAppointmentPeriod();
                var hour = TimeDisplay.ToHour24(h12, period);
                var chosen = new DateTime(year, month, day, hour, minute, 0, new System.Globalization.GregorianCalendar());
                if (chosen > from) from = chosen;
            }

            var (doctorId, specialistId) = SelectedClinicianIds();
            var slot = _smart.SuggestNextOpenSlot(from, 14, doctorId, specialistId, _editingAppointment?.Id);
            if (slot is null)
            {
                MessageBox.Show("لا يوجد موعد متاح خلال الأسبوعين القادمين.", "اقتراح موعد", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            InitializeAppointmentSelectors(slot.Value);
            MessageBox.Show(
                $"أقرب موعد متاح:\n{TimeDisplay.Format12Long(slot.Value)}",
                "اقتراح موعد",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تعذر الاقتراح", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearAppointmentPatientCard(string name)
    {
        AppointmentPatientName.Text = name;
        AppointmentNationalId.Text = AppointmentMobile.Text = AppointmentContact.Text = AppointmentMedical.Text =
            AppointmentGender.Text = AppointmentResidency.Text = AppointmentBloodType.Text = AppointmentChronic.Text =
            AppointmentMedications.Text = AppointmentAllergies.Text = "—";
        ClearSafetyBanner();
    }

    private static string DisplayValue(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private Appointment SaveAppointmentFromForm()
    {
        var fileNumber = AppointmentFileNumberBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(fileNumber))
            throw new InvalidOperationException("أدخل رقم ملف صحيحاً (مثال: A-0001).");
        if (AppointmentYearBox.SelectedItem is not int year || AppointmentMonthBox.SelectedItem is not int month ||
            AppointmentDayBox.SelectedItem is not int day || AppointmentHourBox.SelectedItem is not string hourText ||
            AppointmentMinuteBox.SelectedItem is not string minuteText)
            throw new InvalidOperationException("اختر التاريخ والوقت كاملاً.");
        var period = SelectedAppointmentPeriod();
        var hour24 = TimeDisplay.ToHour24(int.Parse(hourText), period);
        var startsAt = new DateTime(year, month, day, hour24, int.Parse(minuteText), 0,
            new System.Globalization.GregorianCalendar());
        var doctorId = AppointmentDoctorBox.SelectedValue as long?;
        var specialistId = AppointmentSpecialistBox.SelectedValue as long?;
        var staffId = doctorId ?? specialistId;
        var clinicId = AppointmentClinicBox.SelectedValue as long?;
        var kind = AppointmentRenewedBox.IsChecked == true ? "renewed" : "regular";
        return _editingAppointment is null
            ? _appointments.Add(fileNumber, startsAt, AppointmentNotesBox.Text, staffId, doctorId, specialistId, clinicId, kind)
            : _appointments.Update(_editingAppointment.Id, fileNumber, startsAt, AppointmentNotesBox.Text, staffId, doctorId, specialistId, clinicId, kind);
    }

    private void SaveAppointmentInline_Click(object sender, RoutedEventArgs e) => SaveAppointmentInline(false);
    private void SaveAndPrintAppointmentInline_Click(object sender, RoutedEventArgs e) => SaveAppointmentInline(true);

    private void SaveAppointmentInline(bool print)
    {
        try
        {
            var wasEditing = _editingAppointment is not null;
            var appointment = SaveAppointmentFromForm();
            AppointmentFormOverlay.Visibility = Visibility.Collapsed;
            RefreshAll();
            if (print) TryPrintAppointment(appointment);
            else MessageBox.Show(wasEditing ? "تم تعديل الموعد بنجاح." : "تم حفظ الموعد بنجاح.",
                "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
            _editingAppointment = null;
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




    private (long? DoctorId, long? SpecialistId) SelectedClinicianIds()
    {
        long? doctor = AppointmentDoctorBox?.SelectedValue is long d ? d : null;
        long? specialist = AppointmentSpecialistBox?.SelectedValue is long s ? s : null;
        return (doctor, specialist);
    }

    private void SelectLanguageCombo()
    {
        if (UiLanguageBox is null) return;
        foreach (ComboBoxItem item in UiLanguageBox.Items)
        {
            if ((item.Tag as string) == Loc.Code)
            {
                UiLanguageBox.SelectedItem = item;
                break;
            }
        }
        if (UiLanguageBox.SelectedItem is null && UiLanguageBox.Items.Count > 0)
            UiLanguageBox.SelectedIndex = 0;
    }

    private void ApplyShellLanguage()
    {
        Loc.ApplyToWindow(this);
        Title = string.IsNullOrWhiteSpace(ClinicNameBox.Text) ? Loc.T("app.title") : ClinicNameBox.Text;
        if (NavDashboardBtn is not null) NavDashboardBtn.Content = "⌂  " + Loc.T("nav.dashboard");
        if (NavPatientsBtn is not null) NavPatientsBtn.Content = "♙  " + Loc.T("nav.patients");
        if (NavAppointmentsBtn is not null) NavAppointmentsBtn.Content = "◷  " + Loc.T("nav.appointments");
        if (NavSearchBtn is not null) NavSearchBtn.Content = "⌕  " + Loc.T("nav.search");
        if (NavSettingsBtn is not null) NavSettingsBtn.Content = "⚙  " + Loc.T("nav.settings");
        if (TimelineTitleText is not null) TimelineTitleText.Text = Loc.T("dash.timeline");
        if (WalkInButton is not null) WalkInButton.Content = Loc.T("dash.walkin");
    }


    private void LoadClinicLogo()
    {
        try
        {
            if (File.Exists(AppPaths.ClinicLogoFile))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(AppPaths.ClinicLogoFile);
                bmp.EndInit();
                ClinicLogoImage.Source = bmp;
                ClinicLogoImage.Visibility = Visibility.Visible;
                DefaultMark.Visibility = Visibility.Collapsed;
            }
            else
            {
                ClinicLogoImage.Source = null;
                ClinicLogoImage.Visibility = Visibility.Collapsed;
                DefaultMark.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            ClinicLogoImage.Visibility = Visibility.Collapsed;
            DefaultMark.Visibility = Visibility.Visible;
        }
    }

    private void ChooseClinicLogo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "ملفات الصور|*.png;*.jpg;*.jpeg;*.bmp",
            Title = "اختيار شعار العيادة"
        };
        if (dlg.ShowDialog() != true) return;
        File.Copy(dlg.FileName, AppPaths.ClinicLogoFile, true);
        LoadClinicLogo();
    }

    private void ClearClinicLogo_Click(object sender, RoutedEventArgs e)
    {
        try { if (File.Exists(AppPaths.ClinicLogoFile)) File.Delete(AppPaths.ClinicLogoFile); } catch { }
        LoadClinicLogo();
    }


    private void PatientTabBasic_Click(object sender, RoutedEventArgs e)
    {
        PatientBasicPanel.Visibility = Visibility.Visible;
        PatientMedicalPanel.Visibility = Visibility.Collapsed;
    }

    private void PatientTabMedical_Click(object sender, RoutedEventArgs e)
    {
        PatientBasicPanel.Visibility = Visibility.Collapsed;
        PatientMedicalPanel.Visibility = Visibility.Visible;
    }

    private void TeamDayGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TeamDayGrid.SelectedItem is TeamDayRow row)
        {
            TimelineStaffFilterBox.SelectedValue = row.StaffId;
            RefreshTimelineAndToday();
        }
    }

    private void TodayInRoom_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TodayRow row)
        {
            _appointments.SetVisitStage(row.Appointment.Id, "in_room");
            RefreshAll();
        }
    }

    private void WalkIn_Click(object sender, RoutedEventArgs e)
    {
        var slot = _smart.SuggestNextOpenSlot(DateTime.Now, 14, null, null, null) ?? DateTime.Now;
        AddAppointment_Click(sender, e);
        InitializeAppointmentSelectors(slot);
    }

    private void TodayWaiting_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TodayRow row)
        {
            _appointments.SetVisitStage(row.Appointment.Id, "waiting");
            RefreshAll();
        }
    }

    private void ApplyAppointmentFilter()
    {
        var tag = (AppointmentFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Today";
        var filter = tag switch
        {
            "NextWeek" => AppointmentFilter.NextWeek,
            "Missed" => AppointmentFilter.Missed,
            "Renewed" => AppointmentFilter.Renewed,
            "AfterThreeMonths" => AppointmentFilter.AfterThreeMonths,
            "AllUpcoming" => AppointmentFilter.AllUpcoming,
            _ => AppointmentFilter.Today
        };
        AppointmentsGrid.ItemsSource = _appointments.ListByFilter(filter);
    }

    private void AppointmentFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) ApplyAppointmentFilter();
    }

    private void LoadStaffCombos()
    {
        var list = _staff.ListActive();
        AppointmentStaffBox.ItemsSource = list;
        TimelineStaffFilterBox.ItemsSource = list;
        AppointmentDoctorBox.ItemsSource = _staff.ListDoctors();
        AppointmentSpecialistBox.ItemsSource = _staff.ListSpecialists();
        AppointmentClinicBox.ItemsSource = _clinics.ListActive();
        StaffGrid.ItemsSource = _staff.ListAll()
            .Select(s => new StaffEditRow { Id = s.Id, FullName = s.FullName, RoleDisplay = s.RoleDisplay })
            .ToList();
        ClinicsGrid.ItemsSource = _clinics.ListAll()
            .Select(c => new ClinicEditRow { Id = c.Id, Name = c.Name, IsActive = c.IsActive })
            .ToList();
        if (AppointmentClinicBox.Items.Count > 0 && AppointmentClinicBox.SelectedIndex < 0)
            AppointmentClinicBox.SelectedIndex = 0;
    }

    private long? SelectedTimelineStaffId => TimelineStaffFilterBox.SelectedValue as long?;

    private void RefreshTimelineAndToday()
    {
        var staffId = SelectedTimelineStaffId;
        DailyTimelineItems.ItemsSource = _appointments.BuildTimeline(_timelineDay, staffId);
        TodayActionsGrid.ItemsSource = _appointments.TodayList(_timelineDay, staffId);
        if (TeamDayGrid is not null)
            TeamDayGrid.ItemsSource = _appointments.BuildTeamDay(_timelineDay);
        if (TimelineTitleText is not null)
            TimelineTitleText.Text = "الجدول الزمني — " + _timelineDay.ToString("yyyy-MM-dd");
        var usage = _clinics.TodayUsageLabel();
        if (ClinicUsageText is not null) ClinicUsageText.Text = usage;
        if (SettingsClinicUsageText is not null) SettingsClinicUsageText.Text = usage;
    }

    private void TimelinePrev_Click(object sender, RoutedEventArgs e)
    {
        _timelineDay = _timelineDay.AddDays(-1);
        RefreshTimelineAndToday();
    }

    private void TimelineToday_Click(object sender, RoutedEventArgs e)
    {
        _timelineDay = DateTime.Today;
        RefreshTimelineAndToday();
    }

    private void TimelineNext_Click(object sender, RoutedEventArgs e)
    {
        _timelineDay = _timelineDay.AddDays(1);
        RefreshTimelineAndToday();
    }

    private void TimelineStaffFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) RefreshTimelineAndToday();
    }

    private void TimelineSlot_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TimelineSlot slot) return;
        if (slot.Kind == "break")
        {
            MessageBox.Show("هذه الفترة استراحة ولا يمكن الحجز فيها.", "استراحة", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (slot.Kind == "booked")
        {
            MessageBox.Show(slot.PatientName ?? "موعد محجوز", "موعد محجوز");
            return;
        }
        AddAppointment_Click(sender, e);
        InitializeAppointmentSelectors(slot.StartsAt);
    }

    private void TodayCheckIn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TodayRow row) return;
        _appointments.SetStatus(row.Appointment.Id, "completed");
        _appointments.SetVisitStage(row.Appointment.Id, "");
        RefreshAll();
        var follow = MessageBox.Show(Loc.T("act.followup.body"), Loc.T("act.followup"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (follow == MessageBoxResult.Yes)
        {
            AddAppointment_Click(sender, e);
            AppointmentFileNumberBox.Text = row.Appointment.FileNumber;
            AppointmentRenewedBox.IsChecked = true;
            var next = _smart.SuggestNextOpenSlot(
                DateTime.Now.AddDays(1), 14, row.Appointment.DoctorId, row.Appointment.SpecialistId)
                ?? DateTime.Now.AddDays(14);
            InitializeAppointmentSelectors(next);
        }
    }

    private void TodayNoShow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TodayRow row)
        {
            _appointments.SetStatus(row.Appointment.Id, "missed");
            RefreshAll();
        }
    }

    private void TodayCancel_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TodayRow row)
        {
            _appointments.SetStatus(row.Appointment.Id, "cancelled");
            RefreshAll();
        }
    }

    private void SaveStaffNames_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (StaffGrid.ItemsSource is IEnumerable<StaffEditRow> items)
            {
                foreach (var s in items)
                    _staff.Rename(s.Id, s.FullName);
            }
            LoadStaffCombos();
            MessageBox.Show("تم حفظ أسماء الأطباء والأخصائيين.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تعذر الحفظ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddClinic_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _clinics.Add(NewClinicNameBox.Text);
            NewClinicNameBox.Clear();
            LoadStaffCombos();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تعذر إضافة العيادة", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteClinic_Click(object sender, RoutedEventArgs e)
    {
        if (ClinicsGrid.SelectedItem is not ClinicEditRow row) return;
        if (MessageBox.Show($"حذف العيادة «{row.Name}»؟", "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            _clinics.SoftDelete(row.Id);
            LoadStaffCombos();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تعذر الحذف", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshBreakHint()
    {
        var hours = ClinicHours.Load(_settings);
        BreakHoursHint.Text = hours.BreakEnabled
            ? $"الاستراحة: {hours.BreakLabel} — لا يُسمح بالحجز خلالها (تعديلها من الإعدادات)"
            : "الاستراحة غير مفعّلة — يمكن الحجز طوال ساعات الدوام";
    }

    private void LoadWorkingHoursControls()
    {
        WorkStartHourBox.ItemsSource = TimeDisplay.Hour12Options;
        WorkEndHourBox.ItemsSource = TimeDisplay.Hour12Options;
        WorkStartMinuteBox.ItemsSource = ClinicHours.FineMinuteOptions();
        WorkEndMinuteBox.ItemsSource = ClinicHours.FineMinuteOptions();
        BreakStartHourBox.ItemsSource = TimeDisplay.Hour12Options;
        BreakEndHourBox.ItemsSource = TimeDisplay.Hour12Options;
        BreakStartMinuteBox.ItemsSource = ClinicHours.FineMinuteOptions();
        BreakEndMinuteBox.ItemsSource = ClinicHours.FineMinuteOptions();

        var hours = ClinicHours.Load(_settings);
        var (startH12, startP) = TimeDisplay.FromHour24((int)hours.Start.TotalHours);
        var (endH12, endP) = TimeDisplay.FromHour24((int)hours.End.TotalHours);
        if (hours.End.TotalHours == 0)
            (endH12, endP) = (12, "ص");
        WorkStartHourBox.SelectedItem = startH12.ToString();
        WorkEndHourBox.SelectedItem = endH12.ToString();
        WorkStartMinuteBox.SelectedItem = hours.Start.Minutes.ToString("00");
        WorkEndMinuteBox.SelectedItem = hours.End.Minutes.ToString("00");
        WorkStartPeriodBox.SelectedIndex = startP == "م" ? 1 : 0;
        WorkEndPeriodBox.SelectedIndex = endP == "م" ? 1 : 0;

        var (bStartH12, bStartP) = TimeDisplay.FromHour24((int)hours.BreakStart.TotalHours);
        var (bEndH12, bEndP) = TimeDisplay.FromHour24((int)hours.BreakEnd.TotalHours);
        BreakStartHourBox.SelectedItem = bStartH12.ToString();
        BreakEndHourBox.SelectedItem = bEndH12.ToString();
        BreakStartMinuteBox.SelectedItem = hours.BreakStart.Minutes.ToString("00");
        BreakEndMinuteBox.SelectedItem = hours.BreakEnd.Minutes.ToString("00");
        BreakStartPeriodBox.SelectedIndex = bStartP == "م" ? 1 : 0;
        BreakEndPeriodBox.SelectedIndex = bEndP == "م" ? 1 : 0;
        BreakEnabledBox.IsChecked = hours.BreakEnabled;

        foreach (ComboBoxItem item in SlotMinutesBox.Items)
        {
            if (item.Content?.ToString() == hours.SlotMinutes.ToString())
            {
                SlotMinutesBox.SelectedItem = item;
                break;
            }
        }
        if (SlotMinutesBox.SelectedItem is null && SlotMinutesBox.Items.Count > 1)
            SlotMinutesBox.SelectedIndex = 1;
    }


    private void RefreshClosures()
    {
        if (ClosureListBox is not null)
            ClosureListBox.ItemsSource = _settings.ListClosures();
    }

    private void AddClosure_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!DateOnly.TryParseExact(ClosureDateBox.Text.Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                throw new InvalidOperationException("أدخل التاريخ الميلادي بصيغة yyyy-MM-dd.");
            _settings.AddClosure(day, ClosureReasonBox.Text);
            ClosureDateBox.Clear();
            ClosureReasonBox.Clear();
            RefreshClosures();
            RefreshAll();
            MessageBox.Show("تم حفظ يوم الإغلاق.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تعذر الحفظ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoveClosure_Click(object sender, RoutedEventArgs e)
    {
        if (ClosureListBox.SelectedItem is not ClosureDay selected)
        {
            MessageBox.Show("اختر يوم إغلاق من القائمة أولاً.", "أيام الإغلاق", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"حذف يوم الإغلاق {selected.Day:yyyy-MM-dd}؟", "تأكيد الحذف",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            _settings.RemoveClosure(selected.Day);
            RefreshClosures();
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تعذر الحذف", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var startH12 = int.Parse(WorkStartHourBox.SelectedItem as string ?? "8");
            var endH12 = int.Parse(WorkEndHourBox.SelectedItem as string ?? "5");
            var startPeriod = (WorkStartPeriodBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ص";
            var endPeriod = (WorkEndPeriodBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "م";
            var startMin = int.Parse(WorkStartMinuteBox.SelectedItem as string ?? "00");
            var endMin = int.Parse(WorkEndMinuteBox.SelectedItem as string ?? "00");
            var start = new TimeSpan(TimeDisplay.ToHour24(startH12, startPeriod), startMin, 0);
            var end = new TimeSpan(TimeDisplay.ToHour24(endH12, endPeriod), endMin, 0);
            if (end <= start)
                throw new InvalidOperationException("ساعة نهاية الدوام يجب أن تكون بعد ساعة البداية.");

            var slotText = (SlotMinutesBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "15";
            if (!int.TryParse(slotText, out var slot)) slot = 15;

            var breakOn = BreakEnabledBox.IsChecked == true;
            var breakStartH12 = int.Parse(BreakStartHourBox.SelectedItem as string ?? "12");
            var breakEndH12 = int.Parse(BreakEndHourBox.SelectedItem as string ?? "12");
            var breakStartPeriod = (BreakStartPeriodBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "م";
            var breakEndPeriod = (BreakEndPeriodBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "م";
            var breakStartMin = int.Parse(BreakStartMinuteBox.SelectedItem as string ?? "00");
            var breakEndMin = int.Parse(BreakEndMinuteBox.SelectedItem as string ?? "55");
            var breakStart = new TimeSpan(TimeDisplay.ToHour24(breakStartH12, breakStartPeriod), breakStartMin, 0);
            var breakEnd = new TimeSpan(TimeDisplay.ToHour24(breakEndH12, breakEndPeriod), breakEndMin, 0);
            if (breakOn && breakEnd <= breakStart)
                throw new InvalidOperationException("نهاية الاستراحة يجب أن تكون بعد بدايتها.");
            if (breakOn && (breakStart < start || breakEnd > end))
                throw new InvalidOperationException("الاستراحة يجب أن تكون داخل ساعات الدوام.");

            var configuredHours = new ClinicHours(start, end, slot, breakStart, breakEnd, breakOn);
            if (configuredHours.SlotStarts().Count == 0)
                throw new InvalidOperationException("إعدادات الدوام والاستراحة لا تترك أي خانة متاحة للحجز.");

            _settings.Set("clinic_name", ClinicNameBox.Text);
            Loc.Save(_settings);
            configuredHours.Save(_settings);
            Loc.ApplyToWindow(this);
            ApplyShellLanguage();

            RefreshBreakHint();
            InitializeAppointmentSelectors();

            var name = ClinicNameBox.Text.Trim();
            Title = string.IsNullOrWhiteSpace(name) ? "نظام سجلات المرضى" : name;

            var breakLine = breakOn
                ? $"\nالاستراحة: {TimeDisplay.Format12(breakStart)} – {TimeDisplay.Format12(breakEnd)}"
                : "\nالاستراحة: غير مفعّلة";
            MessageBox.Show(
                $"تم حفظ الإعدادات.\nالدوام: {TimeDisplay.Format12(start)} – {TimeDisplay.Format12(end)}{breakLine}\nمدة الفترة: {slot} دقيقة",
                "تم الحفظ",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تعذر الحفظ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateSlotStatus()
    {
        try
        {
            if (AppointmentYearBox.SelectedItem is not int year ||
                AppointmentMonthBox.SelectedItem is not int month ||
                AppointmentDayBox.SelectedItem is not int day ||
                AppointmentHourBox.SelectedItem is not string hourText ||
                AppointmentMinuteBox.SelectedItem is not string minuteText)
            {
                SlotStatusText.Text = "اختر التاريخ والوقت لمعرفة حالة الموعد";
                SlotStatusBanner.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xEE, 0xF2, 0xF7));
                return;
            }

            var period = SelectedAppointmentPeriod();
            var hour24 = TimeDisplay.ToHour24(int.Parse(hourText), period);
            var startsAt = new DateTime(year, month, day, hour24, int.Parse(minuteText), 0,
                new System.Globalization.GregorianCalendar());
            var exceptId = _editingAppointment?.Id;
            var status = _appointments.DescribeSlot(startsAt, exceptId,
                AppointmentDoctorBox.SelectedValue as long?, AppointmentSpecialistBox.SelectedValue as long?);

            SlotStatusText.Text = status switch
            {
                "متاح" => $"✓ متاح — يمكن الحجز في {TimeDisplay.Format12Long(startsAt)}",
                "محجوز" => $"✕ محجوز — يوجد موعد آخر في {TimeDisplay.Format12Long(startsAt)}",
                _ when status is not null && status.Contains("محجوز") => "✕ " + status,
                _ when status is not null && status.StartsWith("استراحة")
                    => $"✕ استراحة — {status} ولا يمكن الحجز",
                _ => status
            };

            byte r, g, b;
            if (status == "متاح") (r, g, b) = (0xE8, 0xF5, 0xE9);
            else if (status is not null && status.Contains("محجوز")) (r, g, b) = (0xFF, 0xEB, 0xEE);
            else if (status is not null && status.StartsWith("استراحة")) (r, g, b) = (0xE3, 0xF2, 0xFD);
            else (r, g, b) = (0xFF, 0xF3, 0xCD);

            SlotStatusBanner.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(r, g, b));
        }
        catch
        {
            SlotStatusText.Text = "تعذر التحقق من حالة الموعد";
        }
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
