using System;
using System.Linq;
using System.Threading.Tasks;
using ExpenseHub.Api.Common;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Dtos;
using ExpenseHub.Api.Responses;
using ExpenseHub.Api.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Expenses;

[TestClass]
internal sealed class ConcurrencyBoundaryTests
{
    [TestMethod]
    [DataRow("update", ExpenseStatus.Draft, ExpenseStatus.Draft, ExpenseHistoryAction.Updated)]
    [DataRow("submit", ExpenseStatus.Draft, ExpenseStatus.Submitted, ExpenseHistoryAction.Submitted)]
    [DataRow("approve", ExpenseStatus.Submitted, ExpenseStatus.Approved, ExpenseHistoryAction.Approved)]
    [DataRow("reject", ExpenseStatus.Submitted, ExpenseStatus.Rejected, ExpenseHistoryAction.Rejected)]
    [DataRow("pay", ExpenseStatus.Approved, ExpenseStatus.Paid, ExpenseHistoryAction.Paid)]
    public async Task CommandsSendOriginalStampToStoreAndPersistFreshStamp(
        string command, ExpenseStatus initialStatus, ExpenseStatus expectedStatus, ExpenseHistoryAction expectedAction)
    {
        var fixture = new ExpenseFixture();
        Expense original = fixture.Seed(initialStatus);
        CurrentActor owner = ExpenseFixture.Actor("owner", AppRoles.Employee);
        CurrentActor approver = ExpenseFixture.Actor("approver", AppRoles.Approver);

        ServiceResult<ExpenseResponse> result = await (command switch
        {
            "update" => fixture.Service.UpdateAsync(owner, original.Id, ExpenseFixture.Input("Changed expense")),
            "submit" => fixture.Service.SubmitAsync(owner, original.Id),
            "approve" => fixture.Service.ApproveAsync(approver, original.Id),
            "reject" => fixture.Service.RejectAsync(approver, original.Id, new RejectExpenseInput { Justification = "Invalid receipt" }),
            "pay" => fixture.Service.PayAsync(ExpenseFixture.Actor("finance", AppRoles.Finance), original.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        });

        Assert.IsTrue(result.Succeeded);
        (Guid expenseId, Guid originalStamp) = fixture.Store.Updates.Single();
        Assert.AreEqual(original.Id, expenseId);
        Assert.AreEqual(original.ConcurrencyStamp, originalStamp, "The store must compare against the version that was read.");
        Expense saved = fixture.Store.Expenses.Single();
        Assert.AreNotEqual(Guid.Empty, saved.ConcurrencyStamp);
        Assert.AreNotEqual(original.ConcurrencyStamp, saved.ConcurrencyStamp);
        Assert.AreEqual(expectedStatus, saved.Status);
        Assert.AreEqual(expectedAction, fixture.Store.Histories.Single().Action);
        Assert.AreEqual(1, fixture.Store.Saves);
    }
}
