using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

[TestClass]
internal sealed class ReportCalculationTests
{
    private static readonly string[] ExpectedStatuses = ["Draft", "Submitted", "Approved", "Rejected", "Paid"];

    [TestMethod]
    public void MineUsesInclusiveExpenseDatesAndDecimalAmounts()
    {
        ReportRequest report = Request();
        Expense[] expenses = [Item("owner", 100m, ExpenseStatus.Approved, 1), Item("owner", 50m, ExpenseStatus.Approved, 30), Item("owner", 25m, ExpenseStatus.Paid, 15), Item("other", 200m, ExpenseStatus.Approved, 15), Item("owner", 500m, ExpenseStatus.Paid, 31)];
        ReportResult result = ReportCalculator.Calculate(report, expenses, DateTime.UtcNow);
        Assert.AreEqual(3, result.TotalCount);
        Assert.AreEqual(175m, result.TotalAmount);
        Assert.AreEqual(150m, result.ByStatus.Single(row => row.Status == "Approved").Amount);
        report.Scope = "Global";
        Assert.AreEqual(375m, ReportCalculator.Calculate(report, expenses, DateTime.UtcNow).TotalAmount);
    }

    [TestMethod]
    public void EmptyResultIncludesAllFiveStatusesAndZeroTotals()
    {
        ReportResult result = ReportCalculator.Calculate(Request(), [], DateTime.UtcNow);
        Assert.AreEqual(0, result.TotalCount);
        Assert.AreEqual(0m, result.TotalAmount);
        CollectionAssert.AreEquivalent(ExpectedStatuses, result.ByStatus.Select(row => row.Status).ToArray());
        Assert.IsTrue(result.ByStatus.All(row => row.Count == 0 && row.Amount == 0m));
    }

    [TestMethod]
    public void ExcelContainsSavedMetadataFiveStatusesAndNumericTotals()
    {
        ReportRequest report = Request();
        ReportResult result = ReportCalculator.Calculate(report, [Item("owner", 175.25m, ExpenseStatus.Paid, 1)], DateTime.UtcNow);
        using var stream = new MemoryStream(ReportExcel.Create(report, result));
        using var workbook = new XLWorkbook(stream);
        IXLWorksheet sheet = workbook.Worksheet("Report");
        Assert.AreEqual(report.Id.ToString(), sheet.Cell("B1").GetString());
        Assert.AreEqual("Status", sheet.Cell("A7").GetString());
        Assert.AreEqual("Count", sheet.Cell("B7").GetString());
        Assert.AreEqual("Amount", sheet.Cell("C7").GetString());
        Assert.AreEqual(1, sheet.Cell("B13").GetValue<int>());
        Assert.AreEqual(175.25m, sheet.Cell("C13").GetValue<decimal>());
        Assert.AreEqual(XLDataType.Number, sheet.Cell("C12").DataType);
        Assert.AreEqual(result.GeneratedAt.ToString("O"), sheet.Cell("B5").GetString());
    }

    internal static ReportRequest Request() => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = "owner",
        Scope = "Mine",
        FromDate = new DateOnly(2026, 9, 1),
        ToDate = new DateOnly(2026, 9, 30),
        CreatedAt = DateTime.UtcNow,
    };

    private static Expense Item(string owner, decimal amount, ExpenseStatus status, int day) => new()
    {
        OwnerId = owner,
        Amount = amount,
        Status = status,
        ExpenseDate = new DateOnly(2026, 9, 1).AddDays(day - 1),
    };
}
