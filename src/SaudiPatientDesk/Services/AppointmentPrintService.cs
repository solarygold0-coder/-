using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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
        AddRow(group, "رقم الملف", patient.FileNumber.ToString());
        AddRow(group, "رقم الهوية", patient.NationalId);
        AddRow(group, "رقم الجوال", patient.Mobile);
        AddRow(group, "تاريخ الموعد", appointment.StartsAt.ToString("yyyy/MM/dd"));
        AddRow(group, "وقت الموعد", appointment.StartsAt.ToString("HH:mm"));
        AddRow(group, "ملاحظات", string.IsNullOrWhiteSpace(appointment.Notes) ? "لا توجد" : appointment.Notes);
        document.Blocks.Add(table);

        document.Blocks.Add(new Paragraph(new Run($"تاريخ الطباعة: {DateTime.Now:yyyy/MM/dd HH:mm}"))
        {
            FontSize = 11,
            Foreground = Brushes.DimGray,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 32, 0, 0)
        });

        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, $"موعد {patient.FullName}");
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
