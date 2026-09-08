using System;
using System.Drawing;
using System.Windows.Forms;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.UI
{
    public sealed class LoginForm : Form
    {
        private readonly AppSecurity security; private readonly bool setupMode; private readonly string expectedUsername;
        private readonly TextBox username = UiKit.TextBox(40), displayName = UiKit.TextBox(80), password = UiKit.TextBox(64), confirm = UiKit.TextBox(64);
        public SecuritySession Session { get; private set; }

        public LoginForm(AppSecurity security, string expectedUsername)
        {
            this.security = security; this.expectedUsername = expectedUsername; setupMode = !security.IsConfigured;
            Text = setupMode ? "إعداد الحماية لأول مرة" : (expectedUsername == null ? "تسجيل الدخول" : "البرنامج مقفل"); Font = UiKit.NormalFont;
            RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false; MaximizeBox = false; ClientSize = new Size(480, setupMode ? 420 : 320); BackColor = UiKit.Background;
            Controls.Add(new Label { Text = "نظام إدارة سجلات المراجعين", Dock = DockStyle.Top, Height = 62, TextAlign = ContentAlignment.MiddleCenter, BackColor = UiKit.Primary, ForeColor = Color.White, Font = new Font("Tahoma", 16, FontStyle.Bold) });
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, AutoScroll = true };
            if (setupMode) { panel.Controls.Add(UiKit.Label("اسم مدير النظام", true)); panel.Controls.Add(displayName); }
            panel.Controls.Add(UiKit.Label("اسم المستخدم", true)); panel.Controls.Add(username); username.Text = string.IsNullOrEmpty(expectedUsername) ? "admin" : expectedUsername; username.ReadOnly = !string.IsNullOrEmpty(expectedUsername);
            panel.Controls.Add(UiKit.Label(setupMode ? "كلمة مرور من 10 خانات تشمل حرفًا ورقمًا ورمزًا" : "كلمة المرور", true)); password.UseSystemPasswordChar = true; panel.Controls.Add(password);
            if (setupMode) { panel.Controls.Add(UiKit.Label("تأكيد كلمة المرور", false)); confirm.UseSystemPasswordChar = true; panel.Controls.Add(confirm); }
            var login = UiKit.Button(setupMode ? "حفظ وبدء الاستخدام" : "دخول", OnLogin, false); login.Dock = DockStyle.Top; panel.Controls.Add(login); Controls.Add(panel); AcceptButton = login;
        }

        private void OnLogin(object sender, EventArgs e)
        {
            try
            {
                if (setupMode) { if (password.Text != confirm.Text) throw new ArgumentException("كلمتا المرور غير متطابقتين."); Session = security.Configure(displayName.Text, password.Text); }
                else Session = security.Login(username.Text, password.Text);
                if (!string.IsNullOrEmpty(expectedUsername) && Session.Username != expectedUsername) throw new UnauthorizedAccessException("يجب إدخال بيانات المستخدم نفسه لفك القفل.");
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { UiKit.ShowError(ex.Message); password.SelectAll(); password.Focus(); }
        }
    }
}
