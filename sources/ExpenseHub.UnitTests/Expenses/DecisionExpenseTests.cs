using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using ExpenseHub.Api.Common;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Dtos;
using ExpenseHub.Api.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Expenses;

[TestClass]
internal sealed class DecisionExpenseTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ApproverDecidesSubmittedExpenseWithServerActorAndTimeAndOneHistory(bool reject)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(ExpenseStatus.Submitted);
        CurrentActor actor = ExpenseFixture.Actor("approver", AppRoles.Approver);
        var result = reject
            ? await fixture.Service.RejectAsync(actor, expense.Id, new RejectExpenseInput { Justification = "  Invalid receipt  " })
            : await fixture.Service.ApproveAsync(actor, expense.Id);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(reject ? "Rejected" : "Approved", result.Value.Status);
        Assert.AreEqual("approver", result.Value.DecidedById);
        Assert.AreEqual(ExpenseFixture.UtcNow, result.Value.DecidedAtUtc);
        Assert.AreEqual(ExpenseFixture.UtcNow, result.Value.UpdatedAtUtc);
        Assert.AreEqual(reject ? "Invalid receipt" : null, result.Value.RejectionReason);
        Assert.AreNotEqual(expense.ConcurrencyStamp, fixture.Store.Expenses.Single().ConcurrencyStamp);
        ExpenseHistory history = fixture.Store.Histories.Single();
        Assert.AreEqual(expense.Id, history.ExpenseId);
        Assert.AreEqual(reject ? ExpenseHistoryAction.Rejected : ExpenseHistoryAction.Approved, history.Action);
        Assert.AreEqual(ExpenseStatus.Submitted, history.FromStatus);
        Assert.AreEqual(reject ? ExpenseStatus.Rejected : ExpenseStatus.Approved, history.ToStatus);
        Assert.AreEqual("approver", history.ActorId);
        Assert.AreEqual(ExpenseFixture.UtcNow, history.OccurredAtUtc);
        Assert.AreEqual(reject ? "Invalid receipt" : null, history.Justification);
        Assert.IsNull(history.Changes);
        Assert.AreEqual(1, fixture.Store.Saves);

        var repeated = reject
            ? await fixture.Service.RejectAsync(actor, expense.Id, new RejectExpenseInput { Justification = "Invalid receipt" })
            : await fixture.Service.ApproveAsync(actor, expense.Id);
        Assert.AreEqual(ServiceErrorKind.Conflict, repeated.Error!.Kind);
        Assert.HasCount(1, fixture.Store.Histories);
        Assert.AreEqual(1, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow(9, false)]
    [DataRow(10, true)]
    [DataRow(500, true)]
    [DataRow(501, false)]
    public async Task RejectionRequiresTrimmedJustificationBetweenTenAndFiveHundredCharacters(int length, bool valid)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(ExpenseStatus.Submitted);
        var input = new RejectExpenseInput { Justification = " " + new string('x', length) + " " };
        bool declarativelyValid = Validator.TryValidateObject(input, new ValidationContext(input), new List<ValidationResult>(), true);
        Assert.AreEqual(valid, declarativelyValid);
        var result = await fixture.Service.RejectAsync(ExpenseFixture.Actor("approver", AppRoles.Approver), expense.Id, input);
        Assert.AreEqual(valid, result.Succeeded);
        if (valid)
        {
            Assert.AreEqual(new string('x', length), fixture.Store.Histories.Single().Justification);
            Assert.AreEqual(new string('x', length), fixture.Store.Expenses.Single().RejectionReason);
        }
        else
        {
            Assert.AreEqual(ServiceErrorKind.Validation, result.Error!.Kind);
            Assert.AreEqual(ExpenseStatus.Submitted, fixture.Store.Expenses.Single().Status);
            Assert.IsEmpty(fixture.Store.Histories);
            Assert.AreEqual(0, fixture.Store.Saves);
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("           ")]
    public async Task RejectionRequiresNonblankJustification(string? justification)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(ExpenseStatus.Submitted);
        var result = await fixture.Service.RejectAsync(ExpenseFixture.Actor("approver", AppRoles.Approver), expense.Id,
            new RejectExpenseInput { Justification = justification! });
        Assert.AreEqual(ServiceErrorKind.Validation, result.Error!.Kind);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow(ExpenseStatus.Draft, ServiceErrorKind.NotFound)]
    [DataRow(ExpenseStatus.Approved, ServiceErrorKind.Conflict)]
    [DataRow(ExpenseStatus.Rejected, ServiceErrorKind.Conflict)]
    [DataRow(ExpenseStatus.Paid, ServiceErrorKind.Conflict)]
    public async Task DecisionsHideAnotherOwnerDraftButConflictForFinalOrApprovedState(ExpenseStatus status, ServiceErrorKind expected)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(status);
        CurrentActor actor = ExpenseFixture.Actor("approver", AppRoles.Approver);
        var approve = await fixture.Service.ApproveAsync(actor, expense.Id);
        var reject = await fixture.Service.RejectAsync(actor, expense.Id, new RejectExpenseInput { Justification = "Invalid receipt" });
        Assert.AreEqual(expected, approve.Error!.Kind);
        Assert.AreEqual(expected, reject.Error!.Kind);
        Assert.AreEqual(status, fixture.Store.Expenses.Single().Status);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow(AppRoles.Employee)]
    [DataRow(AppRoles.Finance)]
    [DataRow(AppRoles.Auditor)]
    [DataRow(AppRoles.Admin)]
    [DataRow("Unknown")]
    [DataRow("")]
    public async Task DecisionRequiresApproverInsideService(string role)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(ExpenseStatus.Submitted);
        CurrentActor actor = ExpenseFixture.Actor("other", role.Split(',', StringSplitOptions.RemoveEmptyEntries));
        var approve = await fixture.Service.ApproveAsync(actor, expense.Id);
        var reject = await fixture.Service.RejectAsync(actor, expense.Id, new RejectExpenseInput { Justification = "Invalid receipt" });
        Assert.AreEqual(ServiceErrorKind.Forbidden, approve.Error!.Kind);
        Assert.AreEqual(ServiceErrorKind.Forbidden, reject.Error!.Kind);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow("Approver")]
    [DataRow("Employee,Approver")]
    [DataRow("Admin,Auditor,Employee,Approver,Finance")]
    public async Task AccumulatingRolesNeverAllowsSelfApprovalOrSelfRejection(string roles)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(ExpenseStatus.Submitted);
        CurrentActor actor = ExpenseFixture.Actor("owner", roles.Split(','));
        var approve = await fixture.Service.ApproveAsync(actor, expense.Id);
        var reject = await fixture.Service.RejectAsync(actor, expense.Id, new RejectExpenseInput { Justification = "Invalid receipt" });
        Assert.AreEqual(ServiceErrorKind.Forbidden, approve.Error!.Kind);
        Assert.AreEqual(ServiceErrorKind.Forbidden, reject.Error!.Kind);
        Assert.AreEqual(ExpenseStatus.Submitted, fixture.Store.Expenses.Single().Status);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    public async Task MissingDecisionTargetReturnsNotFound()
    {
        var fixture = new ExpenseFixture();
        CurrentActor actor = ExpenseFixture.Actor("approver", AppRoles.Approver);
        var approve = await fixture.Service.ApproveAsync(actor, Guid.NewGuid());
        var reject = await fixture.Service.RejectAsync(actor, Guid.NewGuid(), new RejectExpenseInput { Justification = "Invalid receipt" });
        Assert.AreEqual(ServiceErrorKind.NotFound, approve.Error!.Kind);
        Assert.AreEqual(ServiceErrorKind.NotFound, reject.Error!.Kind);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DecisionSaveConflictLeavesStatusDecisionAndHistoryUnchanged(bool reject)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(ExpenseStatus.Submitted);
        fixture.Store.ConflictOnSave = true;
        CurrentActor actor = ExpenseFixture.Actor("approver", AppRoles.Approver);
        var result = reject
            ? await fixture.Service.RejectAsync(actor, expense.Id, new RejectExpenseInput { Justification = "Invalid receipt" })
            : await fixture.Service.ApproveAsync(actor, expense.Id);
        Assert.AreEqual(ServiceErrorKind.Conflict, result.Error!.Kind);
        Expense saved = fixture.Store.Expenses.Single();
        Assert.AreEqual(ExpenseStatus.Submitted, saved.Status);
        Assert.IsNull(saved.DecidedAtUtc);
        Assert.IsNull(saved.DecidedById);
        Assert.IsNull(saved.RejectionReason);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.AreEqual(1, fixture.Store.Saves);
    }
}
