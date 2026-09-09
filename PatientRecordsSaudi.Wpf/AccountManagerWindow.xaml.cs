using System.Collections.ObjectModel;
using System.Windows;
using PatientRecordsSaudi.Services;
using FluentWindow = global::Wpf.Ui.Controls.FluentWindow;

namespace PatientRecordsSaudi.Desktop;

public partial class AccountManagerWindow : FluentWindow
{
    private readonly AppSecurity security;
    private readonly SecuritySession session;
    public AccountManagerWindow(AppSecurity security, SecuritySession session) { this.security = security; this.session = session; InitializeComponent(); LoadUsers(); }
    private void LoadUsers() => UsersGrid.ItemsSource = new ObservableCollection<SecurityUserInfo>(security.GetUsers(session));
    private SecurityUserInfo? Selected() => UsersGrid.SelectedItem as SecurityUserInfo;

    private void AddUser_Click(object sender, RoutedEventArgs e)
    {
        var editor = new UserEditorWindow { Owner = this };
        if (editor.ShowDialog() != true) return;
        try { security.AddUser(session, editor.Username, editor.DisplayName, editor.Role, editor.Password); LoadUsers(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private void ResetPassword_Click(object sender, RoutedEventArgs e)
    {
        SecurityUserInfo? user = Selected(); if (user is null) { ShowError("اختر مستخدمًا."); return; }
        var editor = new PasswordWindow("كلمة المرور الجديدة") { Owner = this };
        if (editor.ShowDialog() != true) return;
        try { security.ResetPassword(session, user.Username, editor.Password); MessageBox.Show("تم تحديث كلمة المرور.", "تم", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private void ToggleUser_Click(object sender, RoutedEventArgs e)
    {
        SecurityUserInfo? user = Selected(); if (user is null) { ShowError("اختر مستخدمًا."); return; }
        try { security.SetUserState(session, user.Username, !user.IsActive); LoadUsers(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private static void ShowError(string text) => MessageBox.Show(text, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
}
