using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.UI
{
    public sealed class ClosureDatesForm : Form
    {
        private readonly AppDatabase database; private readonly DateTimeScrollControl date = new DateTimeScrollControl(false); private readonly TextBox reason = UiKit.TextBox(120); private readonly DataGridView grid = UiKit.Grid();
        public ClosureDatesForm(AppDatabase database)
        {
            this.database = database; Text = "الإجازات وأيام إغلاق المنشأة"; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; StartPosition = FormStartPosition.CenterParent; Size = new Size(720, 520); Font = UiKit.NormalFont; BackColor = UiKit.Background;
            var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 125, Padding = new Padding(12), ColumnCount = 3 }; top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20)); top.Controls.Add(UiKit.Label("التاريخ الميلادي", true), 0, 0); top.Controls.Add(UiKit.Label("السبب", true), 1, 0); top.Controls.Add(date, 0, 1); top.Controls.Add(reason, 1, 1); top.Controls.Add(UiKit.Button("إضافة", Add, false), 2, 1); Controls.Add(top);
            UiKit.AddTextColumn(grid, "Date", "التاريخ", 35); UiKit.AddTextColumn(grid, "Reason", "سبب الإغلاق/الإجازة", 65); Controls.Add(grid); grid.BringToFront(); var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.RightToLeft }; bottom.Controls.Add(UiKit.Button("حذف المحدد", Delete, true)); Controls.Add(bottom); LoadData();
        }
        private void LoadData() { grid.DataSource = new BindingList<ClosureDate>(database.GetClosures()); }
        private void Add(object sender, EventArgs e) { try { database.AddClosure(date.Value.Date, reason.Text); reason.Clear(); LoadData(); } catch (Exception ex) { UiKit.ShowError(ex.Message); } }
        private void Delete(object sender, EventArgs e) { ClosureDate c = grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as ClosureDate; if (c != null && UiKit.Confirm("حذف يوم الإغلاق المحدد؟", "تأكيد")) { database.DeleteClosure(c.Id); LoadData(); } }
    }
}
