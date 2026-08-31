using System;
using System.Drawing;
using System.Windows.Forms;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.UI
{
    public sealed class LoginForm : Form
    {
        private readonly AppSecurity security;
        private readonly bool setupMode;
        private readonly TextBox password = new TextBox();
        private readonly TextBox confirm = new TextBox();
        public string Password { get; private set; }

        public LoginForm(AppSecurity security)
        {
            this.security = security; setupMode = !security.IsConfigured;
            Text = setupMode ? "إعداد الحماية لأول مرة" : "تسجيل الدخول";
            Font = UiKit.NormalFont; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
            StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false; MaximizeBox = false; ClientSize = new Size(460, setupMode ? 310 : 245);
            BackColor = UiKit.Background;

            var title = new Label { Text = "نظام إدارة سجلات المرضى", Dock = DockStyle.Top, Height = 62, TextAlign = ContentAlignment.MiddleCenter, BackColor = UiKit.Primary, ForeColor = Color.White, Font = new Font("Tahoma", 16, FontStyle.Bold) };
            Controls.Add(title);
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = setupMode ? 6 : 4 };
            panel.RowStyles.Clear();
            panel.Controls.Add(UiKit.Label(setupMode ? "أنشئ كلمة مرور لحماية وتشفير بيانات المرضى (6 خانات على الأقل). احتفظ بها؛ لا يمكن استعادة البيانات دونها." : "أدخل كلمة المرور لفتح السجلات.", true));
            password.UseSystemPasswordChar = true; password.Font = new Font("Tahoma", 12); password.Dock = DockStyle.Top; password.MaxLength = 64;
            panel.Controls.Add(password);
            if (setupMode)
            {
                panel.Controls.Add(UiKit.Label("تأكيد كلمة المرور", false));
                confirm.UseSystemPasswordChar = true; confirm.Font = new Font("Tahoma", 12); confirm.Dock = DockStyle.Top; confirm.MaxLength = 64;
                panel.Controls.Add(confirm);
            }
            var login = UiKit.Button(setupMode ? "حفظ وبدء الاستخدام" : "دخول", OnLogin, false); login.Dock = DockStyle.Bottom;
            panel.Controls.Add(login);
            Controls.Add(panel); AcceptButton = login;
        }

        private void OnLogin(object sender, EventArgs e)
        {
            if (password.Text.Length < 6) { UiKit.ShowError("كلمة المرور يجب ألا تقل عن 6 خانات."); password.Focus(); return; }
            if (setupMode)
            {
                if (password.Text != confirm.Text) { UiKit.ShowError("كلمتا المرور غير متطابقتين."); confirm.Focus(); return; }
                try { security.Configure(password.Text); }
                catch (Exception ex) { UiKit.ShowError(ex.Message); return; }
            }
            else if (!security.Verify(password.Text))
            {
                UiKit.ShowError("كلمة المرور غير صحيحة."); password.SelectAll(); password.Focus(); return;
            }
            Password = password.Text; DialogResult = DialogResult.OK; Close();
        }
    }
}
