using System;
using System.Drawing;
using System.Windows.Forms;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.UI
{
    public sealed class DateTimeScrollControl : UserControl
    {
        private readonly ComboBox day = new ComboBox();
        private readonly ComboBox month = new ComboBox();
        private readonly ComboBox year = new ComboBox();
        private readonly ComboBox hour = new ComboBox();
        private readonly ComboBox minute = new ComboBox();
        private readonly ComboBox period = new ComboBox();
        private readonly bool includeTime;

        public DateTimeScrollControl(bool includeTime)
        {
            this.includeTime = includeTime; Height = includeTime ? 70 : 36; Dock = DockStyle.Fill; RightToLeft = RightToLeft.Yes;
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, AutoScroll = false };
            SetupCombo(day, 68); SetupCombo(month, 135); SetupCombo(year, 82);
            for (int i = 1; i <= 31; i++) day.Items.Add(i.ToString("00"));
            for (int i = 1; i <= 12; i++) month.Items.Add(SaudiValidation.MonthLabel(i));
            int current = DateTime.Today.Year;
            for (int i = current - 120; i <= current + 10; i++) year.Items.Add(i.ToString());
            flow.Controls.Add(year); flow.Controls.Add(month); flow.Controls.Add(day);
            if (includeTime)
            {
                SetupCombo(period, 64); SetupCombo(minute, 62); SetupCombo(hour, 62);
                period.Items.AddRange(new object[] { "ص", "م" });
                for (int i = 0; i < 60; i += 5) minute.Items.Add(i.ToString("00"));
                for (int i = 1; i <= 12; i++) hour.Items.Add(i.ToString("00"));
                flow.Controls.Add(period); flow.Controls.Add(minute); flow.Controls.Add(new Label { Text = ":", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }); flow.Controls.Add(hour);
            }
            Controls.Add(flow); month.SelectedIndexChanged += delegate { ClampDay(); }; year.SelectedIndexChanged += delegate { ClampDay(); };
            Value = DateTime.Now.AddMinutes(15 - DateTime.Now.Minute % 15);
        }

        private static void SetupCombo(ComboBox c, int width)
        {
            c.DropDownStyle = ComboBoxStyle.DropDownList; c.Font = UiKit.NormalFont; c.Width = width; c.MaxDropDownItems = 12; c.IntegralHeight = false; c.DropDownHeight = 220;
        }

        private void ClampDay()
        {
            int y, m; if (!int.TryParse(year.Text, out y) || month.SelectedIndex < 0) return; m = month.SelectedIndex + 1;
            int max = DateTime.DaysInMonth(y, m); if (day.SelectedIndex + 1 > max) day.SelectedIndex = max - 1;
        }

        public DateTime Value
        {
            get
            {
                int y = int.Parse(year.Text), m = month.SelectedIndex + 1, d = day.SelectedIndex + 1;
                int h = 0, min = 0;
                if (includeTime)
                {
                    h = int.Parse(hour.Text); if (h == 12) h = 0; if (period.SelectedIndex == 1) h += 12; min = int.Parse(minute.Text);
                }
                return new DateTime(y, m, d, h, min, 0);
            }
            set
            {
                year.SelectedItem = value.Year.ToString(); month.SelectedIndex = value.Month - 1; day.SelectedIndex = value.Day - 1;
                if (includeTime)
                {
                    int h = value.Hour % 12; if (h == 0) h = 12; hour.SelectedItem = h.ToString("00");
                    int rounded = (value.Minute / 5) * 5; minute.SelectedItem = rounded.ToString("00"); period.SelectedIndex = value.Hour >= 12 ? 1 : 0;
                }
            }
        }
    }
}
