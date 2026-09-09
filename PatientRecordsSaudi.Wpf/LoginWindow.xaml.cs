using System.Windows;
using PatientRecordsSaudi.Services;
using FluentWindow = global::Wpf.Ui.Controls.FluentWindow;

namespace PatientRecordsSaudi.Desktop;

public partial class LoginWindow : FluentWindow
{
    private readonly AppSecurity security;
    public SecuritySession? Session { get; private set; }
    public LoginWindow(AppSecurity security) { this.security = security; InitializeComponent(); Loaded += (_, _) => UsernameBox.Focus(); }
    private void Login_Click(object sender, RoutedEventArgs e)
    {
        try { Session = security.Login(UsernameBox.Text, PasswordBox.Password); DialogResult = true; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "تعذر الدخول", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading); PasswordBox.Clear(); PasswordBox.Focus(); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
}
