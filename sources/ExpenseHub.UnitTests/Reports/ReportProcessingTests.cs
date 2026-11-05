using System;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

[TestClass]
internal sealed class ReportProcessingTests
{
    [TestMethod]
    public async Task RedeliveryWithNewProcessorHasNoNewPersistedEffect()
    {
        var store = new FakeReportStore();
        ReportRequest report = ReportCalculationTests.Request();
        store.Reports.Add(report);
        store.Expenses.Add(new Expense { OwnerId = "owner", Amount = 175m, ExpenseDate = report.FromDate, Status = ExpenseStatus.Paid });
        await Processor(store).ProcessAsync(report.Id, default);
        Assert.AreEqual("Completed", report.Status);
        Assert.AreEqual(175m, report.Result!.TotalAmount);
        string? saved = report.ResultJson;
        DateTime? completed = report.CompletedAt;
        int saves = store.Saves;
        store.Expenses[0].Amount = 999m;
        await Processor(store).ProcessAsync(report.Id, default);
        Assert.AreEqual(saved, report.ResultJson);
        Assert.AreEqual(completed, report.CompletedAt);
        Assert.AreEqual(saves, store.Saves);
        Assert.AreEqual(1, store.ExpenseReads);
        var service = ReportAuthorizationTests.Service(store, new FakeReportPublisher());
        Assert.AreEqual(175m, (await service.GetAsync(ReportAuthorizationTests.Actor("owner", "Employee"), report.Id, default))!.Result!.TotalAmount);
    }

    [TestMethod]
    public async Task InterruptedProcessingIsResumedRatherThanSkipped()
    {
        var store = new FakeReportStore();
        ReportRequest report = ReportCalculationTests.Request();
        report.Status = "Processing";
        report.StartedAt = DateTime.UtcNow.AddMinutes(-1);
        store.Reports.Add(report);
        await Processor(store).ProcessAsync(report.Id, default);
        Assert.AreEqual("Completed", report.Status);
        Assert.AreEqual(0, report.Result!.TotalCount);
    }

    [TestMethod]
    public async Task PersistenceFailurePropagatesAndCannotBeAcknowledgedAsSuccess()
    {
        var store = new FakeReportStore { FailSave = true };
        ReportRequest report = ReportCalculationTests.Request();
        store.Reports.Add(report);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Processor(store).ProcessAsync(report.Id, default));
        Assert.IsNull(report.ResultJson);
        Assert.AreEqual(0, store.Saves);
    }

    [TestMethod]
    public async Task InvalidPersistedScopeBecomesSanitizedTerminalFailure()
    {
        var store = new FakeReportStore();
        ReportRequest report = ReportCalculationTests.Request();
        report.Scope = "Invalid";
        store.Reports.Add(report);
        await Processor(store).ProcessAsync(report.Id, default);
        Assert.AreEqual("Failed", report.Status);
        Assert.IsNull(report.ResultJson);
        Assert.IsNotNull(report.CompletedAt);
        Assert.AreEqual("invalid_report", report.ErrorCode);
        int saves = store.Saves;
        await Processor(store).ProcessAsync(report.Id, default);
        Assert.AreEqual(saves, store.Saves);
    }

    private static ReportProcessor Processor(FakeReportStore store) => new(store, TimeProvider.System, NullLogger<ReportProcessor>.Instance);
}
