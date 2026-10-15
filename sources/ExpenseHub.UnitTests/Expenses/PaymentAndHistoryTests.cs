using System;
using System.Linq;
using System.Threading.Tasks;
using ExpenseHub.Api.Common;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Dtos;
using ExpenseHub.Api.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Expenses;

[TestClass]
internal sealed class PaymentAndHistoryTests
{
    [TestMethod]
    public async Task FinancePaysApprovedExpenseOnceWithAuthenticatedActorUtcTimeAndExactAmount()
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(ExpenseStatus.Approved);
        CurrentActor actor = ExpenseFixture.Actor("finance", AppRoles.Finance);
        var result = await fixture.Service.PayAsync(actor, expense.Id);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("Paid", result.Value.Status);
        Assert.AreEqual(ExpenseFixture.UtcNow, result.Value.UpdatedAtUtc);
        PaymentRecord payment = fixture.Store.Payments.Single();
        Assert.AreNotEqual(Guid.Empty, payment.Id);
        Assert.AreEqual(expense.Id, payment.ExpenseId);
        Assert.AreEqual(25m, payment.Amount);
        Assert.AreEqual("finance", payment.PaidById);
        Assert.AreEqual(ExpenseFixture.UtcNow, payment.PaidAtUtc);
        Assert.IsNotNull(result.Value.Payment);
        Assert.AreEqual(payment.Id, result.Value.Payment.Id);
        Expense saved = fixture.Store.Expenses.Single();
        Assert.AreEqual(ExpenseStatus.Paid, saved.Status);
        Assert.AreNotEqual(expense.ConcurrencyStamp, saved.ConcurrencyStamp);
        Assert.AreEqual(payment.Id, saved.Payment!.Id);
        ExpenseHistory history = fixture.Store.Histories.Single();
        Assert.AreEqual(expense.Id, history.ExpenseId);
        Assert.AreEqual(ExpenseHistoryAction.Paid, history.Action);
        Assert.AreEqual(ExpenseStatus.Approved, history.FromStatus);
        Assert.AreEqual(ExpenseStatus.Paid, history.ToStatus);
        Assert.AreEqual("finance", history.ActorId);
        Assert.AreEqual(ExpenseFixture.UtcNow, history.OccurredAtUtc);
        Assert.AreEqual(1, fixture.Store.Saves);

        var detail = await fixture.Service.GetAsync(actor, expense.Id);
        Assert.IsTrue(detail.Succeeded);
        Assert.AreEqual(payment.Id, detail.Value.Payment!.Id);
        var repeated = await fixture.Service.PayAsync(actor, expense.Id);
        Assert.AreEqual(ServiceErrorKind.Conflict, repeated.Error!.Kind);
        Assert.HasCount(1, fixture.Store.Payments);
        Assert.HasCount(1, fixture.Store.Histories);
        Assert.AreEqual(1, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow(ExpenseStatus.Draft, ServiceErrorKind.NotFound)]
    [DataRow(ExpenseStatus.Submitted, ServiceErrorKind.Conflict)]
    [DataRow(ExpenseStatus.Rejected, ServiceErrorKind.Conflict)]
    [DataRow(ExpenseStatus.Paid, ServiceErrorKind.Conflict)]
    public async Task PayHidesOtherOwnerDraftAndConflictsForNonApprovedStates(ExpenseStatus status, ServiceErrorKind expected)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(status);
        var result = await fixture.Service.PayAsync(ExpenseFixture.Actor("finance", AppRoles.Finance), expense.Id);
        Assert.AreEqual(expected, result.Error!.Kind);
        Assert.AreEqual(status, fixture.Store.Expenses.Single().Status);
        Assert.IsEmpty(fixture.Store.Payments);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow(AppRoles.Employee)]
    [DataRow(AppRoles.Approver)]
    [DataRow(AppRoles.Auditor)]
    [DataRow(AppRoles.Admin)]
    [DataRow("Unknown")]
    [DataRow("")]
    public async Task PayRequiresFinanceInsideService(string role)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(ExpenseStatus.Approved);
        var result = await fixture.Service.PayAsync(ExpenseFixture.Actor("other", role.Split(',', StringSplitOptions.RemoveEmptyEntries)), expense.Id);
        Assert.AreEqual(ServiceErrorKind.Forbidden, result.Error!.Kind);
        Assert.IsEmpty(fixture.Store.Payments);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow("Finance")]
    [DataRow("Employee,Finance")]
    [DataRow("Admin,Auditor,Employee,Approver,Finance")]
    public async Task AccumulatingRolesNeverAllowsSelfPayment(string roles)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(ExpenseStatus.Approved);
        var result = await fixture.Service.PayAsync(ExpenseFixture.Actor("owner", roles.Split(',')), expense.Id);
        Assert.AreEqual(ServiceErrorKind.Forbidden, result.Error!.Kind);
        Assert.AreEqual(ExpenseStatus.Approved, fixture.Store.Expenses.Single().Status);
        Assert.IsEmpty(fixture.Store.Payments);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    public async Task PayMissingExpenseReturnsNotFound()
    {
        var fixture = new ExpenseFixture();
        var result = await fixture.Service.PayAsync(ExpenseFixture.Actor("finance", AppRoles.Finance), Guid.NewGuid());
        Assert.AreEqual(ServiceErrorKind.NotFound, result.Error!.Kind);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    public async Task PaymentSaveConflictPersistsNoStatusHistoryOrPaymentAndRetrySavesOnce()
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(ExpenseStatus.Approved);
        fixture.Store.ConflictOnSave = true;
        CurrentActor actor = ExpenseFixture.Actor("finance", AppRoles.Finance);
        var result = await fixture.Service.PayAsync(actor, expense.Id);
        Assert.AreEqual(ServiceErrorKind.Conflict, result.Error!.Kind);
        Assert.AreEqual(ExpenseStatus.Approved, fixture.Store.Expenses.Single().Status);
        Assert.AreEqual(expense.ConcurrencyStamp, fixture.Store.Expenses.Single().ConcurrencyStamp);
        Assert.IsNull(fixture.Store.Expenses.Single().Payment);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.IsEmpty(fixture.Store.Payments);
        Assert.AreEqual(1, fixture.Store.Saves);

        fixture.Store.ConflictOnSave = false;
        var retried = await fixture.Service.PayAsync(actor, expense.Id);
        Assert.IsTrue(retried.Succeeded);
        Assert.HasCount(1, fixture.Store.Histories);
        Assert.HasCount(1, fixture.Store.Payments);
        Assert.AreEqual(2, fixture.Store.Saves);
    }

    [TestMethod]
    public async Task UnexpectedPaymentSaveFailureDoesNotCommitAnyPartOfTransition()
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(ExpenseStatus.Approved);
        fixture.Store.SaveException = new InvalidOperationException("Storage unavailable.");
        CurrentActor actor = ExpenseFixture.Actor("finance", AppRoles.Finance);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.PayAsync(actor, expense.Id));
        Assert.AreEqual(ExpenseStatus.Approved, fixture.Store.Expenses.Single().Status);
        Assert.IsNull(fixture.Store.Expenses.Single().Payment);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.IsEmpty(fixture.Store.Payments);
        Assert.AreEqual(1, fixture.Store.Saves);

        fixture.Store.SaveException = null;
        var retried = await fixture.Service.PayAsync(actor, expense.Id);
        Assert.IsTrue(retried.Succeeded);
        Assert.HasCount(1, fixture.Store.Histories);
        Assert.HasCount(1, fixture.Store.Payments);
    }

    [TestMethod]
    [DataRow("Employee", "owner", ExpenseStatus.Draft, true)]
    [DataRow("Employee", "owner", ExpenseStatus.Submitted, true)]
    [DataRow("Employee", "owner", ExpenseStatus.Approved, true)]
    [DataRow("Employee", "owner", ExpenseStatus.Rejected, true)]
    [DataRow("Employee", "owner", ExpenseStatus.Paid, true)]
    [DataRow("Employee", "other", ExpenseStatus.Draft, false)]
    [DataRow("Employee", "other", ExpenseStatus.Submitted, false)]
    [DataRow("Employee", "other", ExpenseStatus.Approved, false)]
    [DataRow("Employee", "other", ExpenseStatus.Rejected, false)]
    [DataRow("Employee", "other", ExpenseStatus.Paid, false)]
    [DataRow("Approver", "other", ExpenseStatus.Draft, false)]
    [DataRow("Approver", "other", ExpenseStatus.Submitted, true)]
    [DataRow("Approver", "other", ExpenseStatus.Approved, false)]
    [DataRow("Approver", "other", ExpenseStatus.Rejected, false)]
    [DataRow("Approver", "other", ExpenseStatus.Paid, false)]
    [DataRow("Finance", "other", ExpenseStatus.Draft, false)]
    [DataRow("Finance", "other", ExpenseStatus.Submitted, false)]
    [DataRow("Finance", "other", ExpenseStatus.Approved, true)]
    [DataRow("Finance", "other", ExpenseStatus.Rejected, false)]
    [DataRow("Finance", "other", ExpenseStatus.Paid, true)]
    [DataRow("Auditor", "other", ExpenseStatus.Draft, true)]
    [DataRow("Auditor", "other", ExpenseStatus.Submitted, true)]
    [DataRow("Auditor", "other", ExpenseStatus.Approved, true)]
    [DataRow("Auditor", "other", ExpenseStatus.Rejected, true)]
    [DataRow("Auditor", "other", ExpenseStatus.Paid, true)]
    [DataRow("Employee,Approver", "owner", ExpenseStatus.Draft, true)]
    [DataRow("Employee,Approver", "other", ExpenseStatus.Submitted, true)]
    [DataRow("Employee,Approver", "other", ExpenseStatus.Approved, false)]
    [DataRow("Employee,Finance", "owner", ExpenseStatus.Submitted, true)]
    [DataRow("Employee,Finance", "other", ExpenseStatus.Paid, true)]
    [DataRow("Employee,Finance", "other", ExpenseStatus.Draft, false)]
    public async Task HistoryUsesSameVisibilityAsExpense(string roles, string ownerId, ExpenseStatus status, bool visible)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(status, ownerId);
        fixture.Store.Histories.Add(new ExpenseHistory
        {
            Id = 1,
            ExpenseId = expense.Id,
            Action = ExpenseHistoryAction.Created,
            ActorId = ownerId,
            OccurredAtUtc = ExpenseFixture.UtcNow.AddDays(-1),
            ToStatus = ExpenseStatus.Draft,
        });
        CurrentActor actor = ExpenseFixture.Actor("owner", roles.Split(','));
        var result = await fixture.Service.GetHistoryAsync(actor, expense.Id);
        Assert.AreEqual(visible, result.Succeeded);
        if (visible)
        {
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(expense.Id, result.Value.Single().ExpenseId);
            Assert.AreEqual(ownerId, result.Value.Single().ActorId);
        }
        else
        {
            Assert.AreEqual(ServiceErrorKind.NotFound, result.Error!.Kind);
        }

        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow("Admin")]
    [DataRow("Unknown")]
    [DataRow("")]
    public async Task HistoryRequiresFunctionalReadingRoleEvenForOwner(string role)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed();
        var result = await fixture.Service.GetHistoryAsync(ExpenseFixture.Actor("owner", role.Split(',', StringSplitOptions.RemoveEmptyEntries)), expense.Id);
        Assert.AreEqual(ServiceErrorKind.Forbidden, result.Error!.Kind);
    }

    [TestMethod]
    public async Task MissingHistoryExpenseReturnsNotFoundEvenForAuditor()
    {
        var fixture = new ExpenseFixture();
        var result = await fixture.Service.GetHistoryAsync(ExpenseFixture.Actor("auditor", AppRoles.Auditor), Guid.NewGuid());
        Assert.AreEqual(ServiceErrorKind.NotFound, result.Error!.Kind);
    }

    [TestMethod]
    public async Task FullPaidLifecycleProducesOrderedAuditTrailWithServerActorsAndStates()
    {
        var fixture = new ExpenseFixture();
        CurrentActor owner = ExpenseFixture.Actor("owner", AppRoles.Employee);
        var created = await fixture.Service.CreateAsync(owner, ExpenseFixture.Input());
        Assert.IsTrue(created.Succeeded);
        Guid id = created.Value.Id;
        Assert.IsTrue((await fixture.Service.UpdateAsync(owner, id, ExpenseFixture.Input("Changed expense", 30m))).Succeeded);
        Assert.IsTrue((await fixture.Service.SubmitAsync(owner, id)).Succeeded);
        Assert.IsTrue((await fixture.Service.ApproveAsync(ExpenseFixture.Actor("approver", AppRoles.Approver), id)).Succeeded);
        Assert.IsTrue((await fixture.Service.PayAsync(ExpenseFixture.Actor("finance", AppRoles.Finance), id)).Succeeded);

        var history = await fixture.Service.GetHistoryAsync(ExpenseFixture.Actor("auditor", AppRoles.Auditor), id);
        Assert.IsTrue(history.Succeeded);
        Assert.AreEqual("Created,Updated,Submitted,Approved,Paid", string.Join(',', history.Value.Select(entry => entry.Action)));
        Assert.AreEqual(",Draft,Draft,Submitted,Approved", string.Join(',', history.Value.Select(entry => entry.FromStatus)));
        Assert.AreEqual("Draft,Draft,Submitted,Approved,Paid", string.Join(',', history.Value.Select(entry => entry.ToStatus)));
        Assert.AreEqual("owner,owner,owner,approver,finance", string.Join(',', history.Value.Select(entry => entry.ActorId)));
        Assert.IsTrue(history.Value.All(entry => entry.ExpenseId == id && entry.OccurredAtUtc == ExpenseFixture.UtcNow));
        Assert.AreEqual("Description: Valid expense -> Changed expense; Amount: 25 -> 30", history.Value[1].Changes);
        Assert.AreEqual(30m, fixture.Store.Payments.Single().Amount);
        Assert.AreEqual(5, fixture.Store.Saves);
    }

    [TestMethod]
    public async Task RejectedLifecycleRetainsJustificationAndCannotSubmitDecideOrPayAgain()
    {
        var fixture = new ExpenseFixture();
        CurrentActor owner = ExpenseFixture.Actor("owner", AppRoles.Employee);
        CurrentActor approver = ExpenseFixture.Actor("approver", AppRoles.Approver);
        var created = await fixture.Service.CreateAsync(owner, ExpenseFixture.Input());
        Assert.IsTrue(created.Succeeded);
        Guid id = created.Value.Id;
        Assert.IsTrue((await fixture.Service.SubmitAsync(owner, id)).Succeeded);
        var rejected = await fixture.Service.RejectAsync(approver, id, new RejectExpenseInput { Justification = "  Invalid receipt  " });
        Assert.IsTrue(rejected.Succeeded);
        Assert.AreEqual(ServiceErrorKind.Conflict, (await fixture.Service.SubmitAsync(owner, id)).Error!.Kind);
        Assert.AreEqual(ServiceErrorKind.Conflict, (await fixture.Service.ApproveAsync(approver, id)).Error!.Kind);
        Assert.AreEqual(ServiceErrorKind.Conflict, (await fixture.Service.PayAsync(ExpenseFixture.Actor("finance", AppRoles.Finance), id)).Error!.Kind);

        var history = await fixture.Service.GetHistoryAsync(owner, id);
        Assert.IsTrue(history.Succeeded);
        Assert.AreEqual("Created,Submitted,Rejected", string.Join(',', history.Value.Select(entry => entry.Action)));
        Assert.AreEqual("Invalid receipt", history.Value[2].Justification);
        Assert.AreEqual("approver", history.Value[2].ActorId);
        Assert.AreEqual(ExpenseFixture.UtcNow, history.Value[2].OccurredAtUtc);
        Assert.IsEmpty(fixture.Store.Payments);
        Assert.AreEqual(3, fixture.Store.Saves);
    }
}
