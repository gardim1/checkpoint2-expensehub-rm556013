using System;
using System.Linq;
using System.Threading.Tasks;
using ExpenseHub.Api.Common;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Expenses;

[TestClass]
internal sealed class SubmitAndQueryTests
{
    [TestMethod]
    public async Task SubmitOwnedDraftSetsServerTimeAndRecordsOneTransition()
    {
        var fixture = new ExpenseFixture();
        Expense draft = fixture.Seed();
        var result = await fixture.Service.SubmitAsync(ExpenseFixture.Actor("owner", AppRoles.Employee), draft.Id);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("Submitted", result.Value.Status);
        Assert.AreEqual(ExpenseFixture.UtcNow, result.Value.SubmittedAtUtc);
        Assert.AreEqual(ExpenseFixture.UtcNow, result.Value.UpdatedAtUtc);
        Expense saved = fixture.Store.Expenses.Single();
        Assert.AreEqual(ExpenseStatus.Submitted, saved.Status);
        Assert.AreNotEqual(draft.ConcurrencyStamp, saved.ConcurrencyStamp);
        ExpenseHistory history = fixture.Store.Histories.Single();
        Assert.AreEqual(draft.Id, history.ExpenseId);
        Assert.AreEqual(ExpenseHistoryAction.Submitted, history.Action);
        Assert.AreEqual(ExpenseStatus.Draft, history.FromStatus);
        Assert.AreEqual(ExpenseStatus.Submitted, history.ToStatus);
        Assert.AreEqual("owner", history.ActorId);
        Assert.AreEqual(ExpenseFixture.UtcNow, history.OccurredAtUtc);
        Assert.AreEqual(1, fixture.Store.Saves);

        var repeated = await fixture.Service.SubmitAsync(ExpenseFixture.Actor("owner", AppRoles.Employee), draft.Id);
        Assert.AreEqual(ServiceErrorKind.Conflict, repeated.Error!.Kind);
        Assert.HasCount(1, fixture.Store.Histories);
        Assert.AreEqual(1, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow(ExpenseStatus.Submitted)]
    [DataRow(ExpenseStatus.Approved)]
    [DataRow(ExpenseStatus.Rejected)]
    [DataRow(ExpenseStatus.Paid)]
    public async Task SubmitOutsideDraftConflictsWithoutMutation(ExpenseStatus status)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(status);
        var result = await fixture.Service.SubmitAsync(ExpenseFixture.Actor("owner", AppRoles.Employee), expense.Id);
        Assert.AreEqual(ServiceErrorKind.Conflict, result.Error!.Kind);
        Assert.AreEqual(status, fixture.Store.Expenses.Single().Status);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    public async Task SubmitAnotherEmployeeDraftOrMissingExpenseIsNotFound()
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed();
        foreach (Guid id in new[] { expense.Id, Guid.NewGuid() })
        {
            var result = await fixture.Service.SubmitAsync(ExpenseFixture.Actor("other", AppRoles.Employee), id);
            Assert.AreEqual(ServiceErrorKind.NotFound, result.Error!.Kind);
        }

        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow(AppRoles.Admin)]
    [DataRow(AppRoles.Auditor)]
    [DataRow(AppRoles.Approver)]
    [DataRow(AppRoles.Finance)]
    [DataRow("Unknown")]
    public async Task SubmitRequiresEmployeeInsideService(string role)
    {
        var fixture = new ExpenseFixture();
        Expense draft = fixture.Seed();
        var result = await fixture.Service.SubmitAsync(ExpenseFixture.Actor("owner", role), draft.Id);
        Assert.AreEqual(ServiceErrorKind.Forbidden, result.Error!.Kind);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow("Employee", "ownDraft,ownSubmitted,ownApproved,ownRejected,ownPaid")]
    [DataRow("Approver", "ownSubmitted,otherSubmitted")]
    [DataRow("Finance", "ownApproved,ownPaid,otherApproved,otherPaid")]
    [DataRow("Auditor", "ownDraft,ownSubmitted,ownApproved,ownRejected,ownPaid,otherDraft,otherSubmitted,otherApproved,otherRejected,otherPaid")]
    [DataRow("Employee,Approver", "ownDraft,ownSubmitted,ownApproved,ownRejected,ownPaid,otherSubmitted")]
    [DataRow("Employee,Finance", "ownDraft,ownSubmitted,ownApproved,ownRejected,ownPaid,otherApproved,otherPaid")]
    [DataRow("Admin,Employee", "ownDraft,ownSubmitted,ownApproved,ownRejected,ownPaid")]
    public async Task ListAndDetailRespectUnionOfRoleScopes(string roles, string visibleNames)
    {
        var fixture = new ExpenseFixture();
        Expense[] expenses = [
            fixture.Seed(ExpenseStatus.Draft), fixture.Seed(ExpenseStatus.Submitted),
            fixture.Seed(ExpenseStatus.Approved), fixture.Seed(ExpenseStatus.Rejected), fixture.Seed(ExpenseStatus.Paid),
            fixture.Seed(ExpenseStatus.Draft, "other"), fixture.Seed(ExpenseStatus.Submitted, "other"),
            fixture.Seed(ExpenseStatus.Approved, "other"), fixture.Seed(ExpenseStatus.Rejected, "other"), fixture.Seed(ExpenseStatus.Paid, "other"),
        ];
        string[] names = ["ownDraft", "ownSubmitted", "ownApproved", "ownRejected", "ownPaid", "otherDraft", "otherSubmitted", "otherApproved", "otherRejected", "otherPaid"];
        string[] expected = visibleNames.Split(',');
        CurrentActor actor = ExpenseFixture.Actor("owner", roles.Split(','));
        var list = await fixture.Service.ListAsync(actor);
        Assert.IsTrue(list.Succeeded);
        CollectionAssert.AreEquivalent(expected, list.Value.Select(expense => names[Array.FindIndex(expenses, candidate => candidate.Id == expense.Id)]).ToArray());

        for (int index = 0; index < expenses.Length; index++)
        {
            var detail = await fixture.Service.GetAsync(actor, expenses[index].Id);
            if (expected.Contains(names[index], StringComparer.Ordinal))
            {
                Assert.IsTrue(detail.Succeeded, names[index]);
                Assert.AreEqual(expenses[index].Id, detail.Value.Id);
            }
            else
            {
                Assert.AreEqual(ServiceErrorKind.NotFound, detail.Error!.Kind, names[index]);
            }
        }

        var missing = await fixture.Service.GetAsync(actor, Guid.NewGuid());
        Assert.AreEqual(ServiceErrorKind.NotFound, missing.Error!.Kind);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow(AppRoles.Admin)]
    [DataRow("Unknown")]
    [DataRow("")]
    public async Task AdminAloneAndActorsWithoutFunctionalRoleCannotRead(string role)
    {
        var fixture = new ExpenseFixture();
        Expense own = fixture.Seed();
        CurrentActor actor = ExpenseFixture.Actor("owner", role.Split(',', StringSplitOptions.RemoveEmptyEntries));
        var list = await fixture.Service.ListAsync(actor);
        var detail = await fixture.Service.GetAsync(actor, own.Id);
        Assert.AreEqual(ServiceErrorKind.Forbidden, list.Error!.Kind);
        Assert.AreEqual(ServiceErrorKind.Forbidden, detail.Error!.Kind);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    public async Task SubmissionSaveConflictPreservesDraftAndHistory()
    {
        var fixture = new ExpenseFixture();
        Expense draft = fixture.Seed();
        fixture.Store.ConflictOnSave = true;
        var result = await fixture.Service.SubmitAsync(ExpenseFixture.Actor("owner", AppRoles.Employee), draft.Id);
        Assert.AreEqual(ServiceErrorKind.Conflict, result.Error!.Kind);
        Assert.AreEqual(ExpenseStatus.Draft, fixture.Store.Expenses.Single().Status);
        Assert.IsNull(fixture.Store.Expenses.Single().SubmittedAtUtc);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.AreEqual(1, fixture.Store.Saves);
    }
}
