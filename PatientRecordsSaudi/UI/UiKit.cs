using System;
using System.Drawing;
using System.Windows.Forms;

namespace PatientRecordsSaudi.UI
{
    internal static class UiKit
    {
        public static readonly Color Primary = Color.FromArgb(17, 94, 89);
        public static readonly Color PrimaryDark = Color.FromArgb(15, 76, 72);
        public static readonly Color Accent = Color.FromArgb(13, 148, 136);
        public static readonly Color Background = Color.FromArgb(245, 247, 249);
        public static readonly Color Danger = Color.FromArgb(185, 28, 28);
        public static readonly Font NormalFont = new Font("Tahoma", 10F, FontStyle.Regular);
        public static readonly Font BoldFont = new Font("Tahoma", 10F, FontStyle.Bold);

        public static Button Button(string text, EventHandler click, bool danger)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(108, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = danger ? Danger : Primary,
                ForeColor = Color.White,
                Font = BoldFont,
                Cursor = Cursors.Hand,
                Margin = new Padding(5)
            };
            b.FlatAppearance.BorderSize = 0;
            if (click != null) b.Click += click;
            return b;
        }

        public static Label Label(string text, bool bold)
        {
            return new Label { Text = text, AutoSize = true, Font = bold ? BoldFont : NormalFont, Anchor = AnchorStyles.Right, Margin = new Padding(5, 10, 5, 5) };
        }

        public static TextBox TextBox(int maxLength)
        {
            return new TextBox { Font = NormalFont, MaxLength = maxLength, Dock = DockStyle.Fill, Margin = new Padding(5), RightToLeft = RightToLeft.Yes };
        }

        public static ComboBox Combo(params string[] items)
        {
            var c = new ComboBox { Font = NormalFont, DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(5), RightToLeft = RightToLeft.Yes };
            c.Items.AddRange(items); if (items.Length > 0) c.SelectedIndex = 0; return c;
        }

        public static DataGridView Grid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                RightToLeft = RightToLeft.Yes,
                Font = NormalFont
            };
            g.ColumnHeadersDefaultCellStyle.Font = BoldFont;
            g.ColumnHeadersDefaultCellStyle.BackColor = Primary;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            g.ColumnHeadersHeight = 38;
            g.EnableHeadersVisualStyles = false;
            g.RowTemplate.Height = 34;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(240, 249, 248);
            return g;
        }

        public static void AddTextColumn(DataGridView grid, string property, string title, float weight)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = property, Name = property, HeaderText = title, FillWeight = weight, SortMode = DataGridViewColumnSortMode.Automatic });
        }

        public static void ShowError(string message)
        {
            MessageBox.Show(message, "تنبيه تحقق", MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }

        public static bool Confirm(string message, string title)
        {
            return MessageBox.Show(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) == DialogResult.Yes;
        }
    }
}
