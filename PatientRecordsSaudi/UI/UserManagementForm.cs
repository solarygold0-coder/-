using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.UI
{
    public sealed class UserManagementForm : Form
    {
        private readonly AppSecurity security; private readonly SecuritySession session; private readonly DataGridView grid = UiKit.Grid();
        public UserManagementForm(AppSecurity security, SecuritySession session)
        {
            this.security = security; this.session = session; Text = "حسابات الموظفين والصلاحيات"; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; Font = UiKit.NormalFont; StartPosition = FormStartPosition.CenterParent; Size = new Size(760, 520); BackColor = UiKit.Background;
            var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            tools.Controls.Add(UiKit.Button("إضافة موظف", AddUser, false)); tools.Controls.Add(UiKit.Button("إعادة تعيين كلمة المرور", ResetPassword, false)); tools.Controls.Add(UiKit.Button("تفعيل/تعطيل", ToggleUser, true)); Controls.Add(tools);
            UiKit.AddTextColumn(grid, "Username", "اسم المستخدم", 25); UiKit.AddTextColumn(grid, "DisplayName", "اسم الموظف", 35); UiKit.AddTextColumn(grid, "Role", "الصلاحية", 22); UiKit.AddTextColumn(grid, "StatusText", "الحالة", 18); Controls.Add(grid); grid.BringToFront(); LoadUsers();
        }
        private SecurityUserInfo Selected() { return grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as SecurityUserInfo; }
        private void LoadUsers() { grid.DataSource = new BindingList<SecurityUserInfo>(security.GetUsers(session)); }
        private void AddUser(object sender, EventArgs e) { using (var f = new UserEditorDialog()) if (f.ShowDialog(this) == DialogResult.OK) { try { security.AddUser(session, f.Username, f.DisplayName, f.Role, f.Password); LoadUsers(); } catch (Exception ex) { UiKit.ShowError(ex.Message); } } }
        private void ResetPassword(object sender, EventArgs e) { SecurityUserInfo u = Selected(); if (u == null) { UiKit.ShowError("اختر مستخدمًا."); return; } using (var f = new PasswordDialog("كلمة المرور الجديدة")) if (f.ShowDialog(this) == DialogResult.OK) { try { security.ResetPassword(session, u.Username, f.Password); MessageBox.Show("تم تحديث كلمة المرور.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch (Exception ex) { UiKit.ShowError(ex.Message); } } }
        private void ToggleUser(object sender, EventArgs e) { SecurityUserInfo u = Selected(); if (u == null) { UiKit.ShowError("اختر مستخدمًا."); return; } try { security.SetUserState(session, u.Username, !u.IsActive); LoadUsers(); } catch (Exception ex) { UiKit.ShowError(ex.Message); } }
    }

    internal sealed class UserEditorDialog : Form
    {
        private readonly TextBox username = UiKit.TextBox(40), display = UiKit.TextBox(80), password = UiKit.TextBox(64), confirm = UiKit.TextBox(64); private readonly ComboBox role = UiKit.Combo("موظف", "قراءة فقط", "مدير");
        public string Username { get { return username.Text.Trim(); } } public string DisplayName { get { return display.Text.Trim(); } } public string Password { get { return password.Text; } } public string Role { get { return role.Text; } }
        public UserEditorDialog()
        {
            Text = "إضافة حساب موظف"; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(460, 390); Font = UiKit.NormalFont; BackColor = UiKit.Background;
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1 }; t.Controls.Add(UiKit.Label("اسم المستخدم", true)); t.Controls.Add(username); t.Controls.Add(UiKit.Label("اسم الموظف", true)); t.Controls.Add(display); t.Controls.Add(UiKit.Label("الصلاحية", true)); t.Controls.Add(role); t.Controls.Add(UiKit.Label("كلمة المرور (10 خانات، حرف ورقم ورمز)", true)); password.UseSystemPasswordChar = true; t.Controls.Add(password); t.Controls.Add(UiKit.Label("تأكيد كلمة المرور", false)); confirm.UseSystemPasswordChar = true; t.Controls.Add(confirm);
            var save = UiKit.Button("حفظ الحساب", Save, false); t.Controls.Add(save); Controls.Add(t); AcceptButton = save;
        }
        private void Save(object sender, EventArgs e) { if (password.Text != confirm.Text) { UiKit.ShowError("كلمتا المرور غير متطابقتين."); return; } if (Username.Length < 3 || DisplayName.Length < 2) { UiKit.ShowError("تحقق من اسم المستخدم واسم الموظف."); return; } DialogResult = DialogResult.OK; Close(); }
    }

    internal sealed class PasswordDialog : Form
    {
        private readonly TextBox password = UiKit.TextBox(64), confirm = UiKit.TextBox(64); public string Password { get { return password.Text; } }
        public PasswordDialog(string title)
        {
            Text = title; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(430, 260); Font = UiKit.NormalFont; BackColor = UiKit.Background;
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1 }; t.Controls.Add(UiKit.Label("كلمة المرور", true)); password.UseSystemPasswordChar = true; t.Controls.Add(password); t.Controls.Add(UiKit.Label("التأكيد", true)); confirm.UseSystemPasswordChar = true; t.Controls.Add(confirm); var save = UiKit.Button("حفظ", Save, false); t.Controls.Add(save); Controls.Add(t); AcceptButton = save;
        }
        private void Save(object sender, EventArgs e) { if (password.Text.Length < 10) { UiKit.ShowError("كلمة المرور يجب ألا تقل عن 10 خانات."); return; } if (password.Text != confirm.Text) { UiKit.ShowError("كلمتا المرور غير متطابقتين."); return; } DialogResult = DialogResult.OK; Close(); }
    }

    public sealed class ChangePasswordDialog : Form
    {
        private readonly AppSecurity security; private readonly SecuritySession session; private readonly TextBox current = UiKit.TextBox(64), next = UiKit.TextBox(64), confirm = UiKit.TextBox(64);
        public ChangePasswordDialog(AppSecurity security, SecuritySession session)
        {
            this.security = security; this.session = session; Text = "تغيير كلمة المرور"; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(440, 320); Font = UiKit.NormalFont; BackColor = UiKit.Background;
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1 }; t.Controls.Add(UiKit.Label("كلمة المرور الحالية", true)); current.UseSystemPasswordChar = true; t.Controls.Add(current); t.Controls.Add(UiKit.Label("كلمة المرور الجديدة", true)); next.UseSystemPasswordChar = true; t.Controls.Add(next); t.Controls.Add(UiKit.Label("التأكيد", true)); confirm.UseSystemPasswordChar = true; t.Controls.Add(confirm); var save = UiKit.Button("تغيير", Save, false); t.Controls.Add(save); Controls.Add(t); AcceptButton = save;
        }
        private void Save(object sender, EventArgs e) { try { if (next.Text != confirm.Text) throw new ArgumentException("كلمتا المرور غير متطابقتين."); security.ChangePassword(session, current.Text, next.Text); MessageBox.Show("تم تغيير كلمة المرور.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information); DialogResult = DialogResult.OK; Close(); } catch (Exception ex) { UiKit.ShowError(ex.Message); } }
    }
}
