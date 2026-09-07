using System;
using System.Windows;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.Wpf.Views
{
    public partial class LoginWindow : Window
    {
        private readonly AppSecurity security; private readonly bool setup;
        public SecuritySession Session { get; private set; }
        public LoginWindow(AppSecurity security)
        {
            InitializeComponent(); this.security = security; setup = !security.IsConfigured;
            if (setup) { Title = "إعداد الحماية لأول مرة"; ModeText.Text = "إنشاء حساب المدير الأول"; UsernamePanel.Visibility = Visibility.Collapsed; SubmitButton.Content = "إنشاء وفتح البرنامج"; }
            else { DisplayPanel.Visibility = Visibility.Collapsed; ConfirmPanel.Visibility = Visibility.Collapsed; }
            Loaded += delegate { if (setup) DisplayNameBox.Focus(); else UsernameBox.Focus(); };
        }
        private void Submit_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = "";
            try
            {
                if (setup) { if (PasswordBox.Password != ConfirmBox.Password) throw new ArgumentException("كلمتا المرور غير متطابقتين."); Session = security.Configure(DisplayNameBox.Text, PasswordBox.Password); }
                else Session = security.Login(UsernameBox.Text, PasswordBox.Password);
                DialogResult = true;
            }
            catch (Exception ex) { ErrorText.Text = ex.Message; PasswordBox.Clear(); ConfirmBox.Clear(); PasswordBox.Focus(); }
        }
        private void Close_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
    }
}
