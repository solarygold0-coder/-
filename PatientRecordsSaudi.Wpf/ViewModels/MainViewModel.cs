using System;
using System.Collections.ObjectModel;
using System.Linq;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.Wpf.ViewModels
{
    public sealed class MainViewModel : ViewModelBase
    {
        private readonly AppDatabase database;
        private string searchText = "", searchMode = "الكل", sortMode = "رقم الملف", statusText = "جاهز";
        private bool includeArchived, includeCompleted;
        private Patient selectedPatient; private Appointment selectedAppointment; private PatientTask selectedTask; private Patient selectedInventoryPatient;
        private int activePatientCount, todayAppointmentCount, upcomingAppointmentCount, openTaskCount, inventoryCount;

        public MainViewModel(AppDatabase database, SecuritySession session)
        {
            this.database = database; Session = session;
            SearchModes = new ObservableCollection<string>(new[] { "الكل", "رقم الملف", "الهوية/الإقامة", "الاسم", "رقم الجوال", "المدينة" });
            SortModes = new ObservableCollection<string>(new[] { "رقم الملف", "الاسم", "الأحدث", "آخر مراجعة" });
            RefreshAll();
        }

        public SecuritySession Session { get; private set; }
        public string ClinicName { get { return database.GetSettings().ClinicName; } }
        public string UserCaption { get { return Session.DisplayName + " — " + Session.Role; } }
        public string TodayCaption { get { DateTime d = DateTime.Now; return SaudiValidation.ArabicDayName(d) + "، " + d.Day.ToString("00") + " - " + SaudiValidation.MonthLabel(d.Month) + " - " + d.Year; } }
        public ObservableCollection<string> SearchModes { get; private set; }
        public ObservableCollection<string> SortModes { get; private set; }
        public ObservableCollection<Patient> Patients { get; } = new ObservableCollection<Patient>();
        public ObservableCollection<Appointment> Appointments { get; } = new ObservableCollection<Appointment>();
        public ObservableCollection<Appointment> TodayAppointments { get; } = new ObservableCollection<Appointment>();
        public ObservableCollection<PatientTask> Tasks { get; } = new ObservableCollection<PatientTask>();
        public ObservableCollection<PatientTask> DueTasks { get; } = new ObservableCollection<PatientTask>();
        public ObservableCollection<Patient> InventoryPatients { get; } = new ObservableCollection<Patient>();

        public string SearchText { get { return searchText; } set { Set(ref searchText, value); } }
        public string SearchMode { get { return searchMode; } set { if (Set(ref searchMode, value)) RefreshPatients(); } }
        public string SortMode { get { return sortMode; } set { if (Set(ref sortMode, value)) RefreshPatients(); } }
        public bool IncludeArchived { get { return includeArchived; } set { if (Set(ref includeArchived, value)) RefreshPatients(); } }
        public bool IncludeCompleted { get { return includeCompleted; } set { if (Set(ref includeCompleted, value)) RefreshTasks(); } }
        public string StatusText { get { return statusText; } set { Set(ref statusText, value); } }
        public Patient SelectedPatient { get { return selectedPatient; } set { Set(ref selectedPatient, value); } }
        public Appointment SelectedAppointment { get { return selectedAppointment; } set { Set(ref selectedAppointment, value); } }
        public PatientTask SelectedTask { get { return selectedTask; } set { Set(ref selectedTask, value); } }
        public Patient SelectedInventoryPatient { get { return selectedInventoryPatient; } set { Set(ref selectedInventoryPatient, value); } }
        public int ActivePatientCount { get { return activePatientCount; } private set { Set(ref activePatientCount, value); } }
        public int TodayAppointmentCount { get { return todayAppointmentCount; } private set { Set(ref todayAppointmentCount, value); } }
        public int UpcomingAppointmentCount { get { return upcomingAppointmentCount; } private set { Set(ref upcomingAppointmentCount, value); } }
        public int OpenTaskCount { get { return openTaskCount; } private set { Set(ref openTaskCount, value); } }
        public int InventoryCount { get { return inventoryCount; } private set { Set(ref inventoryCount, value); } }

        public void RefreshAll()
        {
            RefreshPatients(); RefreshAppointments(); RefreshTasks(); RefreshInventory();
            ActivePatientCount = database.CountActivePatients(); StatusText = "تم تحديث البيانات في " + DateTime.Now.ToString("HH:mm");
        }

        public void RefreshPatients() { Replace(Patients, database.SearchPatients(SearchMode, SearchText, IncludeArchived, SortMode)); }
        public void RefreshAppointments()
        {
            DateTime today = DateTime.Today; var all = database.GetAppointments(null, null).OrderBy(x => x.StartsAt).ToList();
            Replace(Appointments, all); Replace(TodayAppointments, all.Where(x => x.StartsAt >= today && x.StartsAt < today.AddDays(1)));
            TodayAppointmentCount = TodayAppointments.Count; UpcomingAppointmentCount = all.Count(x => x.StartsAt >= DateTime.Now && x.Status != "ملغي");
        }
        public void RefreshTasks()
        {
            var all = database.GetTasks(IncludeCompleted); Replace(Tasks, all); Replace(DueTasks, all.Where(x => !x.IsCompleted && x.DueAt <= DateTime.Now.AddDays(7)).Take(100)); OpenTaskCount = database.GetTasks(false).Count;
        }
        public void RefreshInventory() { Replace(InventoryPatients, database.GetInventoryCandidates(DateTime.Today)); InventoryCount = InventoryPatients.Count; }
        private static void Replace<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> values) { target.Clear(); foreach (T item in values) target.Add(item); }
    }
}
