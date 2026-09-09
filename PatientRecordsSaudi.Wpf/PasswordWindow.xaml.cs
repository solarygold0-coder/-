using System.Windows;
using FluentWindow = global::Wpf.Ui.Controls.FluentWindow;

namespace PatientRecordsSaudi.Desktop;

public partial class PasswordWindow : FluentWindow
{
    public string Password => PasswordBox.Password;
    public PasswordWindow(string title) { InitializeComponent(); Title = title; TitleText.Text = title; }
    private void Save_Click(object sender, RoutedEventArgs e) { if (PasswordBox.Password != ConfirmBox.Password) { MessageBox.Show("كلمتا المرور غير متطابقتين.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading); return; } DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
