using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Dtos;
using ExpenseHub.Api.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Expenses;

[TestClass]
internal sealed class InputOwnershipTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ClientCannotAssignIdentityStateDecisionPaymentOrAuditFields(bool update)
    {
        const string body = """
            {
              "description": "Changed expense",
              "amount": 30,
              "categoryId": 1,
              "expenseDate": "2026-10-14",
              "id": "11111111-1111-1111-1111-111111111111",
              "ownerId": "client-owner",
              "status": "Paid",
              "createdAtUtc": "2000-01-01T00:00:00Z",
              "updatedAtUtc": "2000-01-01T00:00:00Z",
              "submittedAtUtc": "2000-01-01T00:00:00Z",
              "decidedById": "client-approver",
              "decidedAtUtc": "2000-01-01T00:00:00Z",
              "rejectionReason": "Client rejection",
              "payment": { "amount": 1, "paidById": "client-finance" },
              "actorId": "client-actor",
              "occurredAtUtc": "2000-01-01T00:00:00Z"
            }
            """;
        var fixture = new ExpenseFixture();
        Expense original = fixture.Seed();
        ExpenseInput input = JsonSerializer.Deserialize<ExpenseInput>(body, JsonSerializerOptions.Web)!;
        CurrentActor owner = ExpenseFixture.Actor("owner", AppRoles.Employee);

        var result = update
            ? await fixture.Service.UpdateAsync(owner, original.Id, input)
            : await fixture.Service.CreateAsync(owner, input);

        Assert.IsTrue(result.Succeeded);
        Assert.AreNotEqual(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.Value.Id);
        Assert.AreEqual("owner", result.Value.OwnerId);
        Assert.AreEqual("Draft", result.Value.Status);
        Assert.AreEqual(update ? original.CreatedAtUtc : ExpenseFixture.UtcNow, result.Value.CreatedAtUtc);
        Assert.AreEqual(ExpenseFixture.UtcNow, result.Value.UpdatedAtUtc);
        Assert.IsNull(result.Value.SubmittedAtUtc);
        Assert.IsNull(result.Value.DecidedById);
        Assert.IsNull(result.Value.DecidedAtUtc);
        Assert.IsNull(result.Value.RejectionReason);
        Assert.IsNull(result.Value.Payment);
        Assert.IsEmpty(fixture.Store.Payments);
        ExpenseHistory history = fixture.Store.Histories.Single();
        Assert.AreEqual("owner", history.ActorId);
        Assert.AreEqual(ExpenseFixture.UtcNow, history.OccurredAtUtc);
        Assert.AreEqual(ExpenseStatus.Draft, history.ToStatus);
        Assert.AreEqual(1, fixture.Store.Saves);
    }
}
