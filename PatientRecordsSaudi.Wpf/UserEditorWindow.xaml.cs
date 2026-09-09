using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace PatientRecordsSaudi.Wpf;

public partial class UserEditorWindow : FluentWindow
{
    public string Username => UsernameBox.Text.Trim();
    public string DisplayName => DisplayNameBox.Text.Trim();
    public string Password => PasswordBox.Password;
    public string Role => RoleBox.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? "موظف" : "موظف";
    public UserEditorWindow() => InitializeComponent();
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordBox.Password != ConfirmBox.Password) { ShowError("كلمتا المرور غير متطابقتين."); return; }
        if (Username.Length < 3 || DisplayName.Length < 2) { ShowError("تحقق من اسم المستخدم واسم الموظف."); return; }
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private static void ShowError(string text) => MessageBox.Show(text, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
}
