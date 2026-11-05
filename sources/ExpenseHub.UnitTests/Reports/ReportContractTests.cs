using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

[TestClass]
internal sealed class ReportContractTests
{
    [TestMethod]
    public async Task DownloadUsesSavedResultAndPreservesExcelContractAfterExpenseChanges()
    {
        var store = new FakeReportStore();
        ReportRequest report = ReportCalculationTests.Request();
        ReportResult result = ReportCalculator.Calculate(report, [new Expense { OwnerId = "owner", ExpenseDate = report.ToDate, Amount = 42.75m, Status = ExpenseStatus.Approved }], DateTime.UtcNow);
        report.Status = "Completed";
        report.ResultJson = JsonSerializer.Serialize(result);
        store.Reports.Add(report);
        store.Expenses.Add(new Expense { OwnerId = "owner", Amount = 999m, ExpenseDate = report.ToDate });
        IResult response = await ReportAuthorizationTests.Service(store, new FakeReportPublisher()).ExcelAsync(ReportAuthorizationTests.Actor("owner", "Employee"), report.Id, default);
        var file = response as FileContentHttpResult;
        Assert.IsNotNull(file);
        Assert.AreEqual(ReportExcel.ContentType, file.ContentType);
        Assert.AreEqual($"report-{report.Id}.xlsx", file.FileDownloadName);
        using var stream = new MemoryStream(file.FileContents.ToArray());
        using var book = new XLWorkbook(stream);
        IXLWorksheet sheet = book.Worksheet("Report");
        for (int index = 0; index < 5; index++)
        {
            ReportStatusTotal row = result.ByStatus[index];
            Assert.AreEqual(row.Status, sheet.Cell(index + 8, 1).GetString());
            Assert.AreEqual(row.Count, sheet.Cell(index + 8, 2).GetValue<int>());
            Assert.AreEqual(row.Amount, sheet.Cell(index + 8, 3).GetValue<decimal>());
            Assert.AreEqual(XLDataType.Number, sheet.Cell(index + 8, 3).DataType);
        }

        Assert.AreEqual("Mine", sheet.Cell("B2").GetString());
        Assert.AreEqual("2026-09-01", sheet.Cell("B3").GetString());
        Assert.AreEqual("2026-09-30", sheet.Cell("B4").GetString());
        Assert.AreEqual(42.75m, sheet.Cell("C13").GetValue<decimal>());
        Assert.AreEqual(0, store.ExpenseReads);
    }

    [TestMethod]
    public async Task StorageFailureDoesNotInventRegisteredIdentifierOrPublish()
    {
        var store = new FakeReportStore { FailSave = true };
        var publisher = new FakeReportPublisher();
        ReportCreation result = await ReportAuthorizationTests.Service(store, publisher).CreateAsync(ReportAuthorizationTests.Actor("owner", "Employee"), new ReportInput(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "Mine"), default);
        Assert.AreEqual(503, result.StatusCode);
        Assert.IsNull(result.Report);
        Assert.AreEqual("storage_unavailable", result.Error!.Code);
        Assert.AreEqual(0, publisher.Published);
    }

    [TestMethod]
    public async Task CombinedRolesCanCreateBothScopesAndEachPostHasANewId()
    {
        var store = new FakeReportStore();
        var service = ReportAuthorizationTests.Service(store, new FakeReportPublisher());
        var actor = ReportAuthorizationTests.Actor("owner", "Employee", "Auditor");
        ReportCreation mine = await service.CreateAsync(actor, new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "Mine"), default);
        ReportCreation global = await service.CreateAsync(actor, new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "Global"), default);
        Assert.AreEqual(202, mine.StatusCode);
        Assert.AreEqual(202, global.StatusCode);
        Assert.AreNotEqual(mine.Report!.Id, global.Report!.Id);
        Assert.HasCount(2, await service.ListAsync(actor, default));
    }

    [TestMethod]
    public void EmptyExcelIsValidAndContainsNumericZeroTotals()
    {
        ReportRequest report = ReportCalculationTests.Request();
        using var stream = new MemoryStream(ReportExcel.Create(report, ReportCalculator.Calculate(report, [], DateTime.UtcNow)));
        using var book = new XLWorkbook(stream);
        Assert.AreEqual(0m, book.Worksheet(1).Cell("C13").GetValue<decimal>());
        Assert.AreEqual(XLDataType.Number, book.Worksheet(1).Cell("B13").DataType);
    }
}
