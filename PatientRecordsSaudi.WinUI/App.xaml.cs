using System;
using System.IO;
using Microsoft.UI.Xaml;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.WinUI
{
    public partial class App : Application
    {
        public static string DataDirectory { get; private set; }
        public static MainWindow MainAppWindow { get; private set; }

        public App()
        {
            InitializeComponent();
            UnhandledException += delegate(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
            {
                try { File.AppendAllText(Path.Combine(DataDirectory, "errors-winui.log"), DateTime.UtcNow.ToString("O") + " | " + e.Exception + Environment.NewLine); } catch { }
            };
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaudiPatientRecords");
            Directory.CreateDirectory(DataDirectory); AppDatabase.CleanupTemporaryAttachments();
            MainAppWindow = new MainWindow(); MainAppWindow.Activate();
            await MainAppWindow.InitializeAsync(DataDirectory);
        }
    }
}
