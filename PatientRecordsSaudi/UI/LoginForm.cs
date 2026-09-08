using System;
using System.Drawing;
using System.Windows.Forms;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.UI
{
    public sealed class LoginForm : Form
    {
        private readonly AppSecurity security; private readonly string expectedUsername;
        private readonly TextBox username = UiKit.TextBox(40), password = UiKit.TextBox(64);
        public SecuritySession Session { get; private set; }

        public LoginForm(AppSecurity security, string expectedUsername, bool showDefaultCredentials = false)
        {
            this.security = security; this.expectedUsername = expectedUsername;
            Text = expectedUsername == null ? "تسجيل الدخول" : "البرنامج مقفل"; Font = UiKit.NormalFont;
            RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false; MaximizeBox = false; ClientSize = new Size(480, showDefaultCredentials ? 380 : 320); BackColor = UiKit.Background;
            Controls.Add(new Label { Text = "نظام إدارة سجلات المراجعين", Dock = DockStyle.Top, Height = 62, TextAlign = ContentAlignment.MiddleCenter, BackColor = UiKit.Primary, ForeColor = Color.White, Font = new Font("Tahoma", 16, FontStyle.Bold) });
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, AutoScroll = true };
            if (showDefaultCredentials) panel.Controls.Add(new Label { Text = "الدخول الافتراضي: admin / admin — يجب تغيير كلمة المرور بعد الدخول.", AutoSize = true, ForeColor = UiKit.Danger, Font = UiKit.BoldFont, MaximumSize = new Size(400, 0), Margin = new Padding(3, 3, 3, 12) });
            panel.Controls.Add(UiKit.Label("اسم المستخدم", true)); panel.Controls.Add(username); username.Text = string.IsNullOrEmpty(expectedUsername) ? "admin" : expectedUsername; username.ReadOnly = !string.IsNullOrEmpty(expectedUsername);
            panel.Controls.Add(UiKit.Label("كلمة المرور", true)); password.UseSystemPasswordChar = true; if (showDefaultCredentials) password.Text = "admin"; panel.Controls.Add(password);
            var login = UiKit.Button("دخول", OnLogin, false); login.Dock = DockStyle.Top; panel.Controls.Add(login); Controls.Add(panel); AcceptButton = login;
        }

        private void OnLogin(object sender, EventArgs e)
        {
            try
            {
                Session = security.Login(username.Text, password.Text);
                if (!string.IsNullOrEmpty(expectedUsername) && Session.Username != expectedUsername) throw new UnauthorizedAccessException("يجب إدخال بيانات المستخدم نفسه لفك القفل.");
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { UiKit.ShowError(ex.Message); password.SelectAll(); password.Focus(); }
        }
    }
}
