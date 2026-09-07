using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;
using PatientRecordsSaudi.Wpf.ViewModels;

namespace PatientRecordsSaudi.Wpf.Views
{
    public partial class MainWindow : Window
    {
        private readonly AppDatabase database; private readonly BackupService backups; private readonly SecuritySession session; private readonly MainViewModel viewModel; private readonly DispatcherTimer reminderTimer;
        public MainWindow(AppDatabase database, BackupService backups, SecuritySession session)
        {
            InitializeComponent(); this.database = database; this.backups = backups; this.session = session; viewModel = new MainViewModel(database, session); DataContext = viewModel;
            reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) }; reminderTimer.Tick += delegate { CheckReminders(); }; reminderTimer.Start();
            Loaded += delegate { AnnualInventoryAlert(); CheckReminders(); };
            Closing += OnClosing;
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) { Safe(delegate { viewModel.RefreshAll(); }); }
        private void Search_Click(object sender, RoutedEventArgs e) { Safe(viewModel.RefreshPatients); }
        private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) viewModel.RefreshPatients(); }

        private void AddPatient_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite()) return; var dialog = new PatientEditorWindow(database, null, false) { Owner = this }; if (dialog.ShowDialog() == true) Safe(delegate { Patient saved = database.AddPatient(dialog.Result); viewModel.RefreshAll(); viewModel.SelectedPatient = saved; });
        }
        private void EditPatient_Click(object sender, RoutedEventArgs e) { OpenPatient(viewModel.SelectedPatient); }
        private void PatientGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (!IsButtonSource(e.OriginalSource)) OpenPatient(viewModel.SelectedPatient); }
        private void InventoryGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (!IsButtonSource(e.OriginalSource)) OpenPatient(viewModel.SelectedInventoryPatient); }
        private void AppointmentGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsButtonSource(e.OriginalSource)) return; Appointment a = ((DataGrid)sender).SelectedItem as Appointment; if (a != null) OpenPatient(database.GetPatient(a.PatientId));
        }
        private void TaskGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsButtonSource(e.OriginalSource)) return; PatientTask t = ((DataGrid)sender).SelectedItem as PatientTask; if (t != null) OpenPatient(database.GetPatient(t.PatientId));
        }
        private void OpenPatient_Click(object sender, RoutedEventArgs e)
        {
            Guid id; Button button = sender as Button; if (button != null && Guid.TryParse(Convert.ToString(button.Tag), out id)) OpenPatient(database.GetPatient(id));
        }
        private void OpenInventoryPatient_Click(object sender, RoutedEventArgs e) { OpenPatient(viewModel.SelectedInventoryPatient); }
        private void OpenPatient(Patient patient)
        {
            if (patient == null) { ShowError("اختر مراجعًا أولًا."); return; }
            var dialog = new PatientEditorWindow(database, patient, session.IsReadOnly || patient.IsArchived) { Owner = this };
            if (dialog.ShowDialog() == true && !session.IsReadOnly && !patient.IsArchived) Safe(delegate { database.UpdatePatient(dialog.Result); viewModel.RefreshAll(); });
        }
        private void ArchivePatient_Click(object sender, RoutedEventArgs e) { ArchivePatient(viewModel.SelectedPatient); }
        private void ArchiveInventory_Click(object sender, RoutedEventArgs e) { ArchivePatient(viewModel.SelectedInventoryPatient); }
        private void ArchivePatient(Patient patient)
        {
            if (!RequireAdmin() || patient == null) { if (patient == null) ShowError("اختر مراجعًا أولًا."); return; }
            if (patient.IsArchived) { ShowError("الملف مؤرشف مسبقًا."); return; }
            if (MessageBox.Show("سيُؤرشف الملف رقم " + patient.FileNumber + " ولن يعاد استخدام رقمه. كما ستُغلق مواعيده المستقبلية. هل تريد المتابعة؟", "تأكيد الأرشفة", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                Safe(delegate { database.ArchivePatient(patient.Id, "أرشفة إدارية بعد المراجعة", true); viewModel.RefreshAll(); });
        }

        private void AddAppointment_Click(object sender, RoutedEventArgs e) { ShowAppointmentEditor(null, null); }
        private void AddAppointmentForPatient_Click(object sender, RoutedEventArgs e) { ShowAppointmentEditor(null, viewModel.SelectedPatient == null ? (long?)null : viewModel.SelectedPatient.FileNumber); }
        private void EditAppointment_Click(object sender, RoutedEventArgs e) { ShowAppointmentEditor(viewModel.SelectedAppointment, null); }
        private void ShowAppointmentEditor(Appointment appointment, long? fileNumber)
        {
            if (!CanWrite()) return; if (appointment == null && fileNumber == null && viewModel.SelectedPatient != null) fileNumber = viewModel.SelectedPatient.FileNumber;
            var dialog = new AppointmentEditorWindow(database, appointment, fileNumber) { Owner = this }; if (dialog.ShowDialog() != true) return;
            Safe(delegate { if (appointment == null) database.AddAppointment(dialog.Result); else database.UpdateAppointment(dialog.Result); viewModel.RefreshAll(); });
        }
        private void DeleteAppointment_Click(object sender, RoutedEventArgs e)
        {
            Appointment a = viewModel.SelectedAppointment; if (!RequireAdmin() || a == null) { if (a == null) ShowError("اختر موعدًا أولًا."); return; }
            if (MessageBox.Show("نقل الموعد إلى المحذوفات؟", "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) Safe(delegate { database.DeleteAppointment(a.Id); viewModel.RefreshAll(); });
        }
        private void PrintAppointment_Click(object sender, RoutedEventArgs e)
        {
            Appointment a = viewModel.SelectedAppointment; if (a == null) { ShowError("اختر موعدًا أولًا."); return; } Patient p = database.GetPatient(a.PatientId); AppSettings s = database.GetSettings();
            database.Audit("معاينة طباعة موعد", "Print", a.Id.ToString(), a.FileNumber, a.Title); database.Checkpoint();
            var document = new FlowDocument { FlowDirection = FlowDirection.RightToLeft, FontFamily = new FontFamily("Tahoma"), FontSize = 15, PagePadding = new Thickness(55) };
            document.Blocks.Add(new Paragraph(new Bold(new Run(s.ClinicName))) { FontSize = 22, TextAlignment = TextAlignment.Center });
            document.Blocks.Add(new Paragraph(new Bold(new Run("تأكيد موعد"))) { FontSize = 18, TextAlignment = TextAlignment.Center });
            string identity = p == null ? "" : MaskIdentity(p.NationalId);
            document.Blocks.Add(new Paragraph(new Run("رقم الملف: " + a.FileNumber + "\nاسم المراجع: " + a.PatientName + "\nالهوية/الإقامة: " + identity + "\nالموعد: " + a.Title + " — " + a.VisitType + "\nالتاريخ الميلادي: " + a.DateText + " (" + SaudiValidation.ArabicDayName(a.StartsAt) + ")\nالوقت: " + a.TimeText + "\nالمدة: " + a.DurationMinutes + " دقيقة\nالحالة: " + a.Status)) { LineHeight = 30 });
            var print = new PrintDialog(); if (print.ShowDialog() == true) { document.PageHeight = print.PrintableAreaHeight; document.PageWidth = print.PrintableAreaWidth; print.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "موعد ملف " + a.FileNumber); }
        }

        private void AddTask_Click(object sender, RoutedEventArgs e) { ShowTaskEditor(null, null); }
        private void AddTaskForPatient_Click(object sender, RoutedEventArgs e) { ShowTaskEditor(null, viewModel.SelectedPatient == null ? (long?)null : viewModel.SelectedPatient.FileNumber); }
        private void EditTask_Click(object sender, RoutedEventArgs e) { ShowTaskEditor(viewModel.SelectedTask, null); }
        private void ShowTaskEditor(PatientTask task, long? fileNumber)
        {
            if (!CanWrite()) return; var dialog = new TaskEditorWindow(database, task, fileNumber) { Owner = this }; if (dialog.ShowDialog() != true) return;
            Safe(delegate { if (task == null) database.AddTask(dialog.Result); else database.UpdateTask(dialog.Result); viewModel.RefreshAll(); });
        }
        private void ToggleTask_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite()) return; PatientTask t = viewModel.SelectedTask; if (t == null) { ShowError("اختر مهمة أولًا."); return; } t.IsCompleted = !t.IsCompleted; Safe(delegate { database.UpdateTask(t); viewModel.RefreshAll(); });
        }
        private void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            PatientTask t = viewModel.SelectedTask; if (!RequireAdmin() || t == null) { if (t == null) ShowError("اختر مهمة أولًا."); return; }
            if (MessageBox.Show("نقل المهمة إلى المحذوفات؟", "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) Safe(delegate { database.DeleteTask(t.Id); viewModel.RefreshAll(); });
        }

        private void AnnualInventoryAlert()
        {
            AppSettings settings = database.GetSettings(); int count = database.GetInventoryCandidates(DateTime.Today).Count;
            if (count > 0 && settings.LastInventoryAlertYear != DateTime.Today.Year) { MessageBox.Show("تنبيه الجرد السنوي: يوجد " + count + " ملفًا مضت عشر سنوات دون مراجعة. راجعها من تبويب الجرد قبل الأرشفة.", "الجرد السنوي", MessageBoxButton.OK, MessageBoxImage.Warning); database.SetInventoryAlerted(DateTime.Today.Year); }
        }
        private void CheckReminders()
        {
            DateTime now = DateTime.Now; Appointment a = database.GetNextUnnotifiedAppointment(now, now.AddMinutes(30)); PatientTask t = database.GetNextUnnotifiedTask(now.AddMinutes(-5), now.AddMinutes(30));
            Guid patientId = Guid.Empty; string message = null;
            if (a != null) { database.MarkAppointmentNotified(a.Id); patientId = a.PatientId; message = "موعد قريب: " + a.Title + "\nالمراجع: " + a.PatientName + "\n" + a.TimeText + "\n\nفتح ملف المراجع؟"; }
            else if (t != null) { database.MarkTaskNotified(t.Id); patientId = t.PatientId; message = "تنبيه مهمة: " + t.Title + "\nالمراجع: " + t.PatientName + "\n\nفتح ملف المراجع؟"; }
            if (message != null && MessageBox.Show(message, "تنبيه", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) OpenPatient(database.GetPatient(patientId));
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            reminderTimer.Stop(); try { AppSettings s = database.GetSettings(); if (!string.IsNullOrWhiteSpace(s.AutoBackupDirectory)) backups.CreateBackup(s.AutoBackupDirectory, database); else database.Checkpoint(); } catch { try { database.Checkpoint(); } catch { } } database.Dispose();
        }
        private bool CanWrite() { if (!session.IsReadOnly) return true; ShowError("الحساب بصلاحية قراءة فقط."); return false; }
        private bool RequireAdmin() { if (session.IsAdmin) return true; ShowError("هذه العملية متاحة للمدير فقط."); return false; }
        private void Safe(Action action) { try { action(); } catch (DuplicatePatientException ex) { ShowError(ex.Message + "\nافتح الملف الموجود بدل إنشاء ملف مكرر."); } catch (AppointmentConflictException ex) { ShowError(ex.Message + "\nغيّر اليوم أو الوقت."); } catch (Exception ex) { ShowError(ex.Message); } }
        private static bool IsButtonSource(object source) { DependencyObject d = source as DependencyObject; while (d != null) { if (d is Button) return true; d = VisualTreeHelper.GetParent(d); } return false; }
        private static void ShowError(string text) { MessageBox.Show(text, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning); }
        private static string MaskIdentity(string value) { if (string.IsNullOrEmpty(value) || value.Length < 4) return value ?? ""; return new string('•', value.Length - 4) + value.Substring(value.Length - 4); }
    }
}
