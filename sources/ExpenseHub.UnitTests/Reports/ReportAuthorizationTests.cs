using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Reports;
using ExpenseHub.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

[TestClass]
internal sealed class ReportAuthorizationTests
{
    [TestMethod]
    public async Task InvalidPeriodOrScopeNeverPersistsOrPublishes()
    {
        var store = new FakeReportStore();
        var publisher = new FakeReportPublisher();
        var service = Service(store, publisher);
        ReportInput[] inputs = [new(null, null, "Mine"), new(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 1), "Mine"), new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "Other")];
        foreach (ReportInput input in inputs)
        {
            Assert.AreEqual(400, (await service.CreateAsync(Actor("owner", "Employee"), input, default)).StatusCode);
        }

        Assert.IsEmpty(store.Reports);
        Assert.AreEqual(0, publisher.Published);
    }

    [TestMethod]
    [DataRow("Employee", "Global", 403)]
    [DataRow("Auditor", "Mine", 403)]
    [DataRow("Admin", "Mine", 403)]
    [DataRow("Approver", "Global", 403)]
    [DataRow("Finance", "Mine", 403)]
    [DataRow("Employee", "Mine", 202)]
    [DataRow("Auditor", "Global", 202)]
    public async Task ScopeIsAuthorizedByRole(string role, string scope, int expected)
    {
        var store = new FakeReportStore();
        var publisher = new FakeReportPublisher();
        ReportCreation result = await Service(store, publisher).CreateAsync(Actor("owner", role), Input(scope), default);
        Assert.AreEqual(expected, result.StatusCode);
        Assert.HasCount(expected == 202 ? 1 : 0, store.Reports);
        Assert.AreEqual(expected == 202 ? 1 : 0, publisher.Published);
        if (expected == 202)
        {
            Assert.AreEqual("owner", store.Reports[0].OwnerId);
            Assert.AreEqual(store.Reports[0].Id, publisher.LastId);
        }
    }

    [TestMethod]
    public async Task OtherUserIncludingAuditorCannotListReadOrDownload()
    {
        var store = new FakeReportStore();
        ReportRequest report = ReportCalculationTests.Request();
        store.Reports.Add(report);
        var service = Service(store, new FakeReportPublisher());
        CurrentActor other = Actor("other", "Auditor");
        Assert.IsEmpty(await service.ListAsync(other, default));
        Assert.IsNull(await service.GetAsync(other, report.Id, default));
        Assert.AreEqual(404, ((IStatusCodeHttpResult)await service.ExcelAsync(other, report.Id, default)).StatusCode);
    }

    [TestMethod]
    [DataRow("Pending")]
    [DataRow("Processing")]
    [DataRow("Failed")]
    public async Task OwnUnfinishedDownloadReturnsConflict(string status)
    {
        var store = new FakeReportStore();
        ReportRequest report = ReportCalculationTests.Request();
        report.Status = status;
        store.Reports.Add(report);
        Assert.AreEqual(409, ((IStatusCodeHttpResult)await Service(store, new FakeReportPublisher()).ExcelAsync(Actor("owner", "Employee"), report.Id, default)).StatusCode);
    }

    [TestMethod]
    public async Task UnconfirmedPublicationReturns503WithPersistedIdAndSanitizedError()
    {
        var store = new FakeReportStore();
        var publisher = new FakeReportPublisher { Fail = true };
        ReportCreation result = await Service(store, publisher).CreateAsync(Actor("owner", "Employee"), Input("Mine"), default);
        Assert.AreEqual(503, result.StatusCode);
        Assert.AreEqual(store.Reports[0].Id, result.Report!.Id);
        Assert.AreEqual("Pending", store.Reports[0].Status);
        Assert.AreEqual("publication_unconfirmed", result.Error!.Code);
        Assert.DoesNotContain("secret", result.Error.Message);
    }

    internal static ReportService Service(FakeReportStore store, FakeReportPublisher publisher) => new(store, publisher, TimeProvider.System, NullLogger<ReportService>.Instance);

    internal static CurrentActor Actor(string id, params string[] roles) => new(id, new HashSet<string>(roles));

    private static ReportInput Input(string scope) => new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), scope);
}

internal sealed class FakeReportPublisher : IReportPublisher
{
    public int Published { get; private set; }

    public Guid LastId { get; private set; }

    public bool Fail { get; set; }

    public Task PublishAsync(Guid reportId, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("secret broker credentials");
        }

        Published++;
        LastId = reportId;
        return Task.CompletedTask;
    }
}

