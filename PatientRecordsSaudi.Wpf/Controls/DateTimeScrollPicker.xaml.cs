using System.Globalization;
using System.Windows.Controls;

namespace PatientRecordsSaudi.Wpf.Controls;

public partial class DateTimeScrollPicker : UserControl
{
    private sealed record MonthOption(int Number, string Label);
    private int minimumYear = DateTime.Today.Year - 1;
    private int maximumYear = DateTime.Today.Year + 10;

    public DateTimeScrollPicker()
    {
        InitializeComponent();
        DayBox.ItemsSource = Enumerable.Range(1, 31).Select(x => x.ToString("00", CultureInfo.InvariantCulture));
        string[] names = { "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
        MonthBox.ItemsSource = names.Select((name, index) => new MonthOption(index + 1, (index + 1).ToString("00") + " - " + name));
        HourBox.ItemsSource = Enumerable.Range(0, 24).Select(x => x.ToString("00", CultureInfo.InvariantCulture));
        MinuteBox.ItemsSource = Enumerable.Range(0, 60).Where(x => x % 5 == 0).Select(x => x.ToString("00", CultureInfo.InvariantCulture));
        RebuildYears();
        Value = DateTime.Now;
    }

    public bool ShowTime
    {
        get => TimePanel.Visibility == System.Windows.Visibility.Visible;
        set => TimePanel.Visibility = value ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public DateTime Value
    {
        get
        {
            if (!TryGetValue(out DateTime value, out string error)) throw new InvalidOperationException(error);
            return value;
        }
        set
        {
            if (value.Year < minimumYear || value.Year > maximumYear) ConfigureYearRange(Math.Min(value.Year, minimumYear), Math.Max(value.Year, maximumYear));
            DayBox.SelectedItem = value.Day.ToString("00", CultureInfo.InvariantCulture);
            MonthBox.SelectedValue = value.Month;
            YearBox.SelectedItem = value.Year;
            HourBox.SelectedItem = value.Hour.ToString("00", CultureInfo.InvariantCulture);
            int minute = (int)Math.Round(value.Minute / 5d) * 5;
            if (minute == 60) minute = 55;
            MinuteBox.SelectedItem = minute.ToString("00", CultureInfo.InvariantCulture);
        }
    }

    public void ConfigureYearRange(int minimum, int maximum)
    {
        minimumYear = Math.Min(minimum, maximum);
        maximumYear = Math.Max(minimum, maximum);
        RebuildYears();
    }

    public bool TryGetValue(out DateTime value, out string error)
    {
        value = default;
        error = string.Empty;
        if (!int.TryParse(DayBox.SelectedItem?.ToString(), out int day) || MonthBox.SelectedValue is not int month || YearBox.SelectedItem is not int year)
        {
            error = "اختر اليوم والشهر الميلادي والسنة.";
            return false;
        }

        int hour = 0, minute = 0;
        if (ShowTime && (!int.TryParse(HourBox.SelectedItem?.ToString(), out hour) || !int.TryParse(MinuteBox.SelectedItem?.ToString(), out minute)))
        {
            error = "اختر الساعة والدقيقة.";
            return false;
        }

        try { value = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local); return true; }
        catch (ArgumentOutOfRangeException) { error = "التاريخ الميلادي المحدد غير صحيح لهذا الشهر."; return false; }
    }

    private void RebuildYears()
    {
        int? selected = YearBox.SelectedItem as int?;
        YearBox.ItemsSource = Enumerable.Range(minimumYear, maximumYear - minimumYear + 1).Reverse().ToArray();
        if (selected.HasValue && selected >= minimumYear && selected <= maximumYear) YearBox.SelectedItem = selected.Value;
    }
}
