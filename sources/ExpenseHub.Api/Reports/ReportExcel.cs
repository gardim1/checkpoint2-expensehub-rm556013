using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace ExpenseHub.Api.Reports;

internal static class ReportExcel
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Create(ReportRequest report, ReportResult result)
    {
        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.AddWorksheet("Report");
        sheet.Cell("A1").Value = "Id";
        sheet.Cell("B1").Value = report.Id.ToString();
        sheet.Cell("A2").Value = "Scope";
        sheet.Cell("B2").Value = report.Scope;
        sheet.Cell("A3").Value = "FromDate";
        sheet.Cell("B3").Value = report.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        sheet.Cell("A4").Value = "ToDate";
        sheet.Cell("B4").Value = report.ToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        sheet.Cell("A5").Value = "GeneratedAt";
        sheet.Cell("B5").Value = result.GeneratedAt.ToString("O", CultureInfo.InvariantCulture);
        sheet.Cell("A7").Value = "Status";
        sheet.Cell("B7").Value = "Count";
        sheet.Cell("C7").Value = "Amount";
        int row = 8;
        foreach (ReportStatusTotal total in result.ByStatus)
        {
            sheet.Cell(row, 1).Value = total.Status;
            sheet.Cell(row, 2).Value = total.Count;
            sheet.Cell(row, 3).Value = total.Amount;
            row++;
        }

        sheet.Cell(row, 1).Value = "Total";
        sheet.Cell(row, 2).Value = result.TotalCount;
        sheet.Cell(row, 3).Value = result.TotalAmount;
        sheet.Column(3).Style.NumberFormat.Format = "0.00";
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
