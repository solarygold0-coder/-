using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.UI
{
    public sealed class PatientForm : Form
    {
        private readonly Patient original;
        private readonly ErrorProvider errors = new ErrorProvider();
        private readonly TextBox fileNo = UiKit.TextBox(12), nationalId = UiKit.TextBox(10), fullName = UiKit.TextBox(150), nationality = UiKit.TextBox(50);
        private readonly TextBox mobile = UiKit.TextBox(16), altPhone = UiKit.TextBox(16), city = UiKit.TextBox(50), address = UiKit.TextBox(250);
        private readonly TextBox emergencyName = UiKit.TextBox(100), emergencyPhone = UiKit.TextBox(16);
        private readonly TextBox allergies = UiKit.TextBox(1000), chronic = UiKit.TextBox(1000), notes = UiKit.TextBox(3000);
        private readonly ComboBox identityType = UiKit.Combo("هوية وطنية", "إقامة"), gender = UiKit.Combo("ذكر", "أنثى"), blood = UiKit.Combo("غير محدد", "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-");
        private readonly DateTimeScrollControl birthDate = new DateTimeScrollControl(false);
        public Patient Result { get; private set; }

        public PatientForm(Patient patient, bool readOnly)
        {
            original = patient;
            Text = patient == null ? "إضافة مراجع جديد" : "ملف المراجع رقم " + patient.FileNumber;
            RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; Font = UiKit.NormalFont; BackColor = UiKit.Background;
            StartPosition = FormStartPosition.CenterParent; Size = new Size(900, 700); MinimumSize = new Size(760, 600);
            errors.BlinkStyle = ErrorBlinkStyle.NeverBlink;

            var header = new Label { Text = Text, Dock = DockStyle.Top, Height = 52, TextAlign = ContentAlignment.MiddleCenter, BackColor = UiKit.Primary, ForeColor = Color.White, Font = new Font("Tahoma", 14, FontStyle.Bold) };
            Controls.Add(header);
            var tabs = new TabControl { Dock = DockStyle.Fill, Font = UiKit.BoldFont };
            tabs.TabPages.Add(BuildPersonalTab()); tabs.TabPages.Add(BuildContactTab()); tabs.TabPages.Add(BuildMedicalTab());
            Controls.Add(tabs);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
            buttons.Controls.Add(UiKit.Button("حفظ", Save, false));
            buttons.Controls.Add(UiKit.Button("إلغاء", delegate { DialogResult = DialogResult.Cancel; Close(); }, true));
            Controls.Add(buttons);
            ConfigureInputs(); if (patient != null) LoadPatient(patient); else { nationality.Text = "سعودي"; birthDate.Value = DateTime.Today.AddYears(-30); }
            if (readOnly) { foreach (Control c in AllControls(this)) if (c is TextBox || c is ComboBox || c is DateTimeScrollControl) c.Enabled = false; buttons.Controls[0].Visible = false; Text += " — قراءة فقط"; }
        }

        private static System.Collections.Generic.IEnumerable<Control> AllControls(Control root) { foreach (Control c in root.Controls) { yield return c; foreach (Control child in AllControls(c)) yield return child; } }

        private TabPage BuildPersonalTab()
        {
            var tab = new TabPage("البيانات الأساسية") { BackColor = Color.White, Padding = new Padding(16) };
            var table = FormTable();
            AddRow(table, "رقم الملف", fileNo, "نوع الهوية", identityType);
            AddRow(table, "رقم الهوية/الإقامة", nationalId, "الاسم الكامل", fullName);
            AddRow(table, "الجنس", gender, "تاريخ الميلاد الميلادي", birthDate);
            AddRow(table, "الجنسية", nationality, "فصيلة الدم", blood);
            tab.Controls.Add(table); return tab;
        }
        private TabPage BuildContactTab()
        {
            var tab = new TabPage("التواصل والعنوان") { BackColor = Color.White, Padding = new Padding(16) };
            var table = FormTable();
            AddRow(table, "الجوال", mobile, "هاتف بديل", altPhone);
            AddRow(table, "المدينة", city, "العنوان", address);
            AddRow(table, "اسم جهة الطوارئ", emergencyName, "جوال الطوارئ", emergencyPhone);
            tab.Controls.Add(table); return tab;
        }
        private TabPage BuildMedicalTab()
        {
            var tab = new TabPage("المعلومات الطبية") { BackColor = Color.White, Padding = new Padding(16) };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 78));
            SetupMultiline(allergies, 95); SetupMultiline(chronic, 95); SetupMultiline(notes, 160);
            AddWideRow(table, "الحساسيات الدوائية/الغذائية", allergies);
            AddWideRow(table, "الأمراض المزمنة", chronic);
            AddWideRow(table, "ملاحظات طبية وإدارية", notes);
            tab.Controls.Add(table); return tab;
        }

        private static TableLayoutPanel FormTable()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32)); return t;
        }
        private static void AddRow(TableLayoutPanel t, string l1, Control c1, string l2, Control c2)
        {
            int row = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(UiKit.Label(l1, true), 0, row); t.Controls.Add(c1, 1, row); t.Controls.Add(UiKit.Label(l2, true), 2, row); t.Controls.Add(c2, 3, row);
        }
        private static void AddWideRow(TableLayoutPanel t, string label, Control control)
        {
            int row = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(UiKit.Label(label, true), 0, row); t.Controls.Add(control, 1, row);
        }
        private static void SetupMultiline(TextBox box, int height) { box.Multiline = true; box.ScrollBars = ScrollBars.Vertical; box.Height = height; box.Dock = DockStyle.Top; }

        private void ConfigureInputs()
        {
            fileNo.ReadOnly = true; fileNo.BackColor = Color.FromArgb(235, 238, 240); fileNo.Text = original == null ? "يُنشأ تلقائيًا عند الحفظ" : original.FileNumber.ToString();
            nationalId.KeyPress += DigitsKeyPress; mobile.KeyPress += PhoneKeyPress; altPhone.KeyPress += PhoneKeyPress; emergencyPhone.KeyPress += PhoneKeyPress;
            nationalId.Leave += delegate { ValidateIdentity(false); };
        }

        private static void DigitsKeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
        }
        private static void PhoneKeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '+' && e.KeyChar != '-' && e.KeyChar != ' ') e.Handled = true;
        }

        private bool ValidateIdentity(bool showMessage)
        {
            string err; bool ok = SaudiValidation.ValidateSaudiIdentity(nationalId.Text, identityType.Text, out err);
            errors.SetError(nationalId, ok ? "" : err); if (!ok && showMessage) UiKit.ShowError(err); return ok;
        }

        private bool ValidateAll()
        {
            errors.Clear();
            if (!ValidateIdentity(true)) { nationalId.Focus(); return false; }
            if (string.IsNullOrWhiteSpace(fullName.Text) || fullName.Text.Trim().Length < 4 || !fullName.Text.Any(char.IsLetter))
            { errors.SetError(fullName, "أدخل الاسم الكامل بصورة صحيحة."); UiKit.ShowError("أدخل الاسم الكامل بصورة صحيحة ولا تستخدم أرقامًا فقط."); fullName.Focus(); return false; }
            if (birthDate.Value.Date > DateTime.Today)
            { errors.SetError(birthDate, "تاريخ الميلاد لا يمكن أن يكون في المستقبل."); UiKit.ShowError("تاريخ الميلاد لا يمكن أن يكون في المستقبل."); return false; }
            string err;
            if (!SaudiValidation.ValidateSaudiMobile(mobile.Text, true, out err)) { errors.SetError(mobile, err); UiKit.ShowError(err); mobile.Focus(); return false; }
            if (!SaudiValidation.ValidateSaudiMobile(altPhone.Text, false, out err)) { errors.SetError(altPhone, err); UiKit.ShowError("الهاتف البديل: " + err); altPhone.Focus(); return false; }
            if (!SaudiValidation.ValidateSaudiMobile(emergencyPhone.Text, false, out err)) { errors.SetError(emergencyPhone, err); UiKit.ShowError("جوال الطوارئ: " + err); emergencyPhone.Focus(); return false; }
            if (string.IsNullOrWhiteSpace(city.Text)) { errors.SetError(city, "المدينة مطلوبة."); UiKit.ShowError("أدخل مدينة المراجع."); city.Focus(); return false; }
            return true;
        }

        private void Save(object sender, EventArgs e)
        {
            if (!ValidateAll()) return;
            Result = original ?? new Patient();
            Result.IdentityType = identityType.Text; Result.NationalId = SaudiValidation.NormalizeDigits(nationalId.Text);
            Result.FullName = fullName.Text.Trim(); Result.Gender = gender.Text; Result.DateOfBirth = birthDate.Value.Date; Result.Nationality = nationality.Text.Trim();
            Result.Mobile = SaudiValidation.NormalizeSaudiMobile(mobile.Text); Result.AlternatePhone = SaudiValidation.NormalizeSaudiMobile(altPhone.Text);
            Result.City = city.Text.Trim(); Result.Address = address.Text.Trim(); Result.EmergencyContact = emergencyName.Text.Trim(); Result.EmergencyPhone = SaudiValidation.NormalizeSaudiMobile(emergencyPhone.Text);
            Result.BloodType = blood.Text; Result.Allergies = allergies.Text.Trim(); Result.ChronicConditions = chronic.Text.Trim(); Result.Notes = notes.Text.Trim();
            DialogResult = DialogResult.OK; Close();
        }

        private void LoadPatient(Patient p)
        {
            fileNo.Text = p.FileNumber.ToString(); identityType.SelectedItem = p.IdentityType; nationalId.Text = p.NationalId; fullName.Text = p.FullName;
            gender.SelectedItem = p.Gender; if (p.DateOfBirth.HasValue) birthDate.Value = p.DateOfBirth.Value; nationality.Text = p.Nationality;
            mobile.Text = p.Mobile; altPhone.Text = p.AlternatePhone; city.Text = p.City; address.Text = p.Address;
            emergencyName.Text = p.EmergencyContact; emergencyPhone.Text = p.EmergencyPhone; blood.SelectedItem = p.BloodType;
            allergies.Text = p.Allergies; chronic.Text = p.ChronicConditions; notes.Text = p.Notes;
        }
    }
}
