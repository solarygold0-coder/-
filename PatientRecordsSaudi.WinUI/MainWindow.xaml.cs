using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.WinUI
{
    public sealed partial class MainWindow : Window
    {
        private AppDatabase database; private BackupService backups; private AppSecurity security; private SecuritySession session; private DispatcherQueueTimer reminderTimer; private bool initialized;
        public MainWindow()
        {
            InitializeComponent();
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1380, 850)); Closed += OnClosed;
        }

        public async Task InitializeAsync(string dataDirectory)
        {
            security = new AppSecurity(dataDirectory); session = await LoginAsync(); if (session == null) { Close(); return; }
            database = new AppDatabase(dataDirectory, session.DatabasePassword, session.DisplayName, session.Role); backups = new BackupService(dataDirectory); security.FlushPendingAudit(database); initialized = true;
            Shell.SelectedItem = Shell.MenuItems[0]; LoadAll();
            reminderTimer = DispatcherQueue.CreateTimer(); reminderTimer.Interval = TimeSpan.FromMinutes(1); reminderTimer.Tick += delegate { _ = CheckRemindersAsync(); }; reminderTimer.Start();
            await ShowInventoryAlertAsync(); await CheckRemindersAsync();
        }

        private async Task<SecuritySession> LoginAsync()
        {
            bool setup = !security.IsConfigured; var username = new TextBox { Header = "اسم المستخدم", Text = "admin" }; var display = new TextBox { Header = "اسم المسؤول" }; var password = new PasswordBox { Header = "كلمة المرور" }; var confirm = new PasswordBox { Header = "تأكيد كلمة المرور" };
            var content = new StackPanel { Spacing = 10 }; if (setup) content.Children.Add(display); else content.Children.Add(username); content.Children.Add(password); if (setup) content.Children.Add(confirm);
            while (true)
            {
                var dialog = Dialog(setup ? "إنشاء حساب المدير الأول" : "تسجيل الدخول", content, setup ? "إنشاء وفتح البرنامج" : "دخول", "إغلاق"); ContentDialogResult result = await dialog.ShowAsync(); if (result != ContentDialogResult.Primary) return null;
                try { if (setup) { if (password.Password != confirm.Password) throw new ArgumentException("كلمتا المرور غير متطابقتين."); return security.Configure(display.Text, password.Password); } return security.Login(username.Text, password.Password); }
                catch (Exception ex) { await AlertAsync("تعذر الدخول", ex.Message); password.Password = ""; confirm.Password = ""; }
            }
        }

        private void Shell_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (!(args.SelectedItem is NavigationViewItem item)) return; string tag = Convert.ToString(item.Tag); DashboardPage.Visibility = tag == "dashboard" ? Visibility.Visible : Visibility.Collapsed; PatientsPage.Visibility = tag == "patients" ? Visibility.Visible : Visibility.Collapsed; AppointmentsPage.Visibility = tag == "appointments" ? Visibility.Visible : Visibility.Collapsed; TasksPage.Visibility = tag == "tasks" ? Visibility.Visible : Visibility.Collapsed; InventoryPage.Visibility = tag == "inventory" ? Visibility.Visible : Visibility.Collapsed; SettingsPage.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadAll()
        {
            if (!initialized) return; AppSettings settings = database.GetSettings(); ClinicCaption.Text = settings.ClinicName; UserCaption.Text = session.DisplayName + " — " + session.Role; DateTime d = DateTime.Now; DateCaption.Text = SaudiValidation.ArabicDayName(d) + "، " + d.Day.ToString("00") + " - " + SaudiValidation.MonthLabel(d.Month) + " - " + d.Year;
            List<Patient> patients = database.SearchPatients(Convert.ToString(SearchMode.SelectedItem), SearchText.Text, false, "رقم الملف"); PatientsList.ItemsSource = patients;
            List<Appointment> appointments = database.GetAppointments(null, null); AppointmentsList.ItemsSource = appointments; TodayAppointmentsList.ItemsSource = appointments.Where(x => x.StartsAt.Date == DateTime.Today).ToList();
            List<PatientTask> tasks = database.GetTasks(true); TasksList.ItemsSource = tasks; DueTasksList.ItemsSource = tasks.Where(x => !x.IsCompleted && x.DueAt <= DateTime.Now.AddDays(7)).Take(100).ToList();
            List<Patient> inventory = database.GetInventoryCandidates(DateTime.Today); InventoryList.ItemsSource = inventory; PatientCount.Text = database.CountActivePatients().ToString("N0"); TodayCount.Text = appointments.Count(x => x.StartsAt.Date == DateTime.Today).ToString("N0"); UpcomingCount.Text = appointments.Count(x => x.StartsAt >= DateTime.Now && x.Status != "ملغي").ToString("N0"); TaskCount.Text = tasks.Count(x => !x.IsCompleted).ToString("N0"); InventoryCount.Text = inventory.Count.ToString("N0"); BackupFolder.Text = settings.AutoBackupDirectory ?? "";
        }

        private async void AddPatient_Click(object sender, RoutedEventArgs e) { if (!CanWrite()) return; Patient result = await EditorDialogs.EditPatientAsync(this, database, null, false); if (result != null) LoadAll(); }
        private async void EditPatient_Click(object sender, RoutedEventArgs e) { await OpenPatientAsync(PatientsList.SelectedItem as Patient); }
        private async void Patient_ItemClick(object sender, ItemClickEventArgs e) { await OpenPatientAsync(e.ClickedItem as Patient); }
        private async Task OpenPatientAsync(Patient patient) { if (patient == null) { await AlertAsync("اختر مراجعًا", "حدد ملف المراجع أولًا."); return; } await EditorDialogs.EditPatientAsync(this, database, patient, session.IsReadOnly || patient.IsArchived); LoadAll(); }
        private async void ArchivePatient_Click(object sender, RoutedEventArgs e)
        {
            Patient p = PatientsList.SelectedItem as Patient; if (!RequireAdmin() || p == null) { if (p == null) await AlertAsync("اختر مراجعًا", "حدد ملف المراجع أولًا."); return; } if (await ConfirmAsync("أرشفة الملف", "سيبقى رقم الملف " + p.FileNumber + " محفوظًا ولن يعاد استخدامه. هل تريد المتابعة؟")) { try { database.ArchivePatient(p.Id, "أرشفة إدارية", true); LoadAll(); } catch (Exception ex) { await AlertAsync("تعذر الأرشفة", ex.Message); } }
        }
        private async void AppointmentForPatient_Click(object sender, RoutedEventArgs e) { Patient p = PatientsList.SelectedItem as Patient; if (p == null) { await AlertAsync("اختر مراجعًا", "حدد ملف المراجع أولًا."); return; } if (CanWrite() && await EditorDialogs.EditAppointmentAsync(this, database, null, p.FileNumber) != null) LoadAll(); }
        private async void TaskForPatient_Click(object sender, RoutedEventArgs e) { Patient p = PatientsList.SelectedItem as Patient; if (p == null) { await AlertAsync("اختر مراجعًا", "حدد ملف المراجع أولًا."); return; } if (CanWrite() && await EditorDialogs.EditTaskAsync(this, database, null, p.FileNumber) != null) LoadAll(); }
        private void Search_Click(object sender, RoutedEventArgs e) { LoadAll(); }

        private async void AddAppointment_Click(object sender, RoutedEventArgs e) { if (CanWrite() && await EditorDialogs.EditAppointmentAsync(this, database, null, null) != null) LoadAll(); }
        private async void EditAppointment_Click(object sender, RoutedEventArgs e) { Appointment a = AppointmentsList.SelectedItem as Appointment; if (a == null) { await AlertAsync("اختر موعدًا", "حدد الموعد أولًا."); return; } if (CanWrite() && await EditorDialogs.EditAppointmentAsync(this, database, a, null) != null) LoadAll(); }
        private async void Appointment_ItemClick(object sender, ItemClickEventArgs e) { Appointment a = e.ClickedItem as Appointment; if (a != null) await OpenPatientAsync(database.GetPatient(a.PatientId)); }
        private async void DeleteAppointment_Click(object sender, RoutedEventArgs e) { Appointment a = AppointmentsList.SelectedItem as Appointment; if (!RequireAdmin() || a == null) { if (a == null) await AlertAsync("اختر موعدًا", "حدد الموعد أولًا."); return; } if (await ConfirmAsync("حذف إداري", "نقل الموعد إلى المحذوفات؟")) { database.DeleteAppointment(a.Id); LoadAll(); } }

        private async void AddTask_Click(object sender, RoutedEventArgs e) { if (CanWrite() && await EditorDialogs.EditTaskAsync(this, database, null, null) != null) LoadAll(); }
        private async void EditTask_Click(object sender, RoutedEventArgs e) { PatientTask t = TasksList.SelectedItem as PatientTask; if (t == null) { await AlertAsync("اختر مهمة", "حدد المهمة أولًا."); return; } if (CanWrite() && await EditorDialogs.EditTaskAsync(this, database, t, null) != null) LoadAll(); }
        private async void Task_ItemClick(object sender, ItemClickEventArgs e) { PatientTask t = e.ClickedItem as PatientTask; if (t != null) await OpenPatientAsync(database.GetPatient(t.PatientId)); }
        private async void ToggleTask_Click(object sender, RoutedEventArgs e) { PatientTask t = TasksList.SelectedItem as PatientTask; if (t == null) { await AlertAsync("اختر مهمة", "حدد المهمة أولًا."); return; } if (!CanWrite()) return; try { t.IsCompleted = !t.IsCompleted; database.UpdateTask(t); LoadAll(); } catch (Exception ex) { await AlertAsync("تعذر التعديل", ex.Message); } }
        private async void DeleteTask_Click(object sender, RoutedEventArgs e) { PatientTask t = TasksList.SelectedItem as PatientTask; if (!RequireAdmin() || t == null) { if (t == null) await AlertAsync("اختر مهمة", "حدد المهمة أولًا."); return; } if (await ConfirmAsync("حذف إداري", "نقل المهمة إلى المحذوفات؟")) { database.DeleteTask(t.Id); LoadAll(); } }
        private void Refresh_Click(object sender, RoutedEventArgs e) { LoadAll(); }
        private async void Backup_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return; try { string folder = string.IsNullOrWhiteSpace(BackupFolder.Text) ? System.IO.Path.Combine(database.DataDirectory, "AutoBackups") : BackupFolder.Text.Trim(); string path = backups.CreateBackup(folder, database); await AlertAsync("نجح النسخ الاحتياطي", "حُفظت النسخة في:\n" + path); } catch (Exception ex) { await AlertAsync("فشل النسخ الاحتياطي", ex.Message); }
        }

        private async Task ShowInventoryAlertAsync() { AppSettings s = database.GetSettings(); int count = database.GetInventoryCandidates(DateTime.Today).Count; if (count > 0 && s.LastInventoryAlertYear != DateTime.Today.Year) { await AlertAsync("الجرد السنوي", "يوجد " + count + " ملفًا مضت عشر سنوات دون مراجعة. لا تُحذف تلقائيًا."); database.SetInventoryAlerted(DateTime.Today.Year); } }
        private async Task CheckRemindersAsync()
        {
            if (!initialized) return; DateTime now = DateTime.Now; Appointment a = database.GetNextUnnotifiedAppointment(now, now.AddDays(2)); PatientTask t = database.GetNextUnnotifiedTask(now.AddDays(-7), now.AddMinutes(5));
            if (a != null) { database.MarkAppointmentNotified(a.Id); if (await ConfirmAsync("موعد قريب", a.PatientName + "\n" + a.Title + "\n" + a.DateText + " — " + a.TimeText + "\n\nفتح ملف المراجع؟")) await OpenPatientAsync(database.GetPatient(a.PatientId)); }
            else if (t != null) { database.MarkTaskNotified(t.Id); if (await ConfirmAsync("تنبيه مهمة", t.PatientName + "\n" + t.Title + "\n" + t.DueText + "\n\nفتح ملف المراجع؟")) await OpenPatientAsync(database.GetPatient(t.PatientId)); }
        }

        internal ContentDialog Dialog(string title, object content, string primary, string close) { return new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = close, DefaultButton = ContentDialogButton.Primary, FlowDirection = FlowDirection.RightToLeft }; }
        internal async Task AlertAsync(string title, string message) { await new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, CloseButtonText = "حسنًا", FlowDirection = FlowDirection.RightToLeft }.ShowAsync(); }
        internal async Task<bool> ConfirmAsync(string title, string message) { return await Dialog(title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, "نعم", "لا").ShowAsync() == ContentDialogResult.Primary; }
        private bool CanWrite() { if (session != null && !session.IsReadOnly) return true; _ = AlertAsync("صلاحية قراءة فقط", "هذا الحساب لا يملك تعديل البيانات."); return false; }
        private bool RequireAdmin() { if (session != null && session.IsAdmin) return true; _ = AlertAsync("صلاحية المدير", "هذه العملية متاحة للمدير فقط."); return false; }
        private void OnClosed(object sender, WindowEventArgs args) { if (reminderTimer != null) reminderTimer.Stop(); try { database?.Checkpoint(); database?.Dispose(); } catch { } AppDatabase.CleanupTemporaryAttachments(); }
    }
}
