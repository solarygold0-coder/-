using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Globalization;
using SaudiPatientDesk.Domain;

namespace SaudiPatientDesk.Services;

public sealed class AppointmentPrintService
{
    public void Print(Appointment appointment, Patient patient, string clinicName)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        var document = new FlowDocument
        {
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 15,
            PagePadding = new Thickness(55),
            ColumnWidth = double.PositiveInfinity,
            PageWidth = dialog.PrintableAreaWidth,
            PageHeight = dialog.PrintableAreaHeight
        };

        document.Blocks.Add(new Paragraph(new Run(string.IsNullOrWhiteSpace(clinicName) ? "نظام سجلات المرضى" : clinicName))
        {
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        });
        document.Blocks.Add(new Paragraph(new Run("تأكيد موعد مراجع"))
        {
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(11, 122, 117)),
            Margin = new Thickness(0, 0, 0, 28)
        });

        var table = new Table { CellSpacing = 0 };
        table.Columns.Add(new TableColumn { Width = new GridLength(170) });
        table.Columns.Add(new TableColumn { Width = new GridLength(330) });
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        AddRow(group, "اسم المراجع", patient.FullName);
        AddRow(group, "رقم الملف", patient.FileNumber);
        AddRow(group, "الجنس", patient.GenderDisplay);
        AddRow(group, "رقم الهوية", patient.NationalId);
        if (!string.IsNullOrWhiteSpace(patient.ResidencyNumber))
            AddRow(group, "رقم الإقامة", patient.ResidencyNumber);
        AddRow(group, "رقم الجوال", patient.Mobile);
        if (!string.IsNullOrWhiteSpace(patient.BloodType))
            AddRow(group, "فصيلة الدم", patient.BloodType);
        if (!string.IsNullOrWhiteSpace(patient.DrugAllergies))
            AddRow(group, "تحسس الأدوية", patient.DrugAllergies);
        if (!string.IsNullOrWhiteSpace(patient.ChronicDiseases))
            AddRow(group, "الأمراض المزمنة", patient.ChronicDiseases);
        if (!string.IsNullOrWhiteSpace(patient.CurrentMedications))
            AddRow(group, "الأدوية الحالية", patient.CurrentMedications);
        if (!string.IsNullOrWhiteSpace(patient.BriefMedicalInfo))
            AddRow(group, "معلومات طبية مختصرة", patient.BriefMedicalInfo);
        if (!string.IsNullOrWhiteSpace(appointment.ClinicName))
            AddRow(group, "العيادة", appointment.ClinicName);
        if (appointment.ClinicianSummary != "—")
            AddRow(group, "المعالج", appointment.ClinicianSummary);
        AddRow(group, "تاريخ الموعد", appointment.StartsAt.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
        AddRow(group, "وقت الموعد", SaudiPatientDesk.Services.TimeDisplay.Format12(appointment.StartsAt));
        AddRow(group, "ملاحظات", string.IsNullOrWhiteSpace(appointment.Notes) ? "لا توجد" : appointment.Notes);
        document.Blocks.Add(table);

        document.Blocks.Add(new Paragraph(new Run("تاريخ الطباعة: " +
            DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) + "  " + TimeDisplay.Format12(DateTime.Now)))
        {
            FontSize = 11,
            Foreground = Brushes.DimGray,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 32, 0, 0)
        });

        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, $"موعد {patient.FullName}");
    }

    public void PrintFileCard(Patient patient, string clinicName)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        var rtl = FlowDirection.RightToLeft;
        var document = new FlowDocument
        {
            FlowDirection = rtl,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 16,
            PagePadding = new Thickness(40),
            ColumnWidth = double.PositiveInfinity,
            PageWidth = dialog.PrintableAreaWidth,
            PageHeight = dialog.PrintableAreaHeight
        };
        document.Blocks.Add(new Paragraph(new Run(string.IsNullOrWhiteSpace(clinicName) ? "عيادة" : clinicName))
        { FontSize = 18, TextAlignment = TextAlignment.Center });
        document.Blocks.Add(new Paragraph(new Run("بطاقة المراجع"))
        { FontSize = 14, TextAlignment = TextAlignment.Center, Margin = new Thickness(0,0,0,16) });
        document.Blocks.Add(new Paragraph(new Run(patient.FileNumber))
        { FontSize = 36, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center });
        document.Blocks.Add(new Paragraph(new Run(patient.FullName))
        { FontSize = 20, TextAlignment = TextAlignment.Center, Margin = new Thickness(0,8,0,0) });
        document.Blocks.Add(new Paragraph(new Run(patient.Mobile))
        { FontSize = 16, TextAlignment = TextAlignment.Center, Foreground = Brushes.DimGray });
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, $"بطاقة {patient.FileNumber}");
    }

    private static void AddRow(TableRowGroup group, string label, string? value)
    {
        var row = new TableRow();
        row.Cells.Add(Cell(label, true));
        row.Cells.Add(Cell(value ?? "—", false));
        group.Rows.Add(row);
    }

    private static TableCell Cell(string value, bool label)
    {
        var cell = new TableCell(new Paragraph(new Run(value)))
        {
            Padding = new Thickness(12),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 226, 232)),
            BorderThickness = new Thickness(1),
            Background = label ? new SolidColorBrush(Color.FromRgb(241, 245, 249)) : Brushes.White
        };
        if (label) cell.FontWeight = FontWeights.SemiBold;
        return cell;
    }
}
