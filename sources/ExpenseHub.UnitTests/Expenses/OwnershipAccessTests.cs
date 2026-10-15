using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ExpenseHub.Api.Common;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Expenses;

[TestClass]
internal sealed class OwnershipAccessTests
{
    [TestMethod]
    [DataRow(ExpenseStatus.Draft, AppRoles.Auditor, ServiceErrorKind.Forbidden)]
    [DataRow(ExpenseStatus.Submitted, AppRoles.Approver, ServiceErrorKind.Forbidden)]
    [DataRow(ExpenseStatus.Approved, AppRoles.Finance, ServiceErrorKind.Forbidden)]
    [DataRow(ExpenseStatus.Paid, AppRoles.Finance, ServiceErrorKind.Forbidden)]
    [DataRow(ExpenseStatus.Rejected, AppRoles.Auditor, ServiceErrorKind.Forbidden)]
    [DataRow(ExpenseStatus.Draft, AppRoles.Approver, ServiceErrorKind.NotFound)]
    [DataRow(ExpenseStatus.Draft, AppRoles.Finance, ServiceErrorKind.NotFound)]
    [DataRow(ExpenseStatus.Submitted, AppRoles.Finance, ServiceErrorKind.NotFound)]
    [DataRow(ExpenseStatus.Rejected, AppRoles.Approver, ServiceErrorKind.NotFound)]
    [DataRow(ExpenseStatus.Paid, AppRoles.Approver, ServiceErrorKind.NotFound)]
    [DataRow(ExpenseStatus.Approved, AppRoles.Admin, ServiceErrorKind.NotFound)]
    public async Task NonOwnerEditAndSubmitReturnForbiddenOnlyWhenVisible(ExpenseStatus status, string extraRole, ServiceErrorKind expected)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(status);
        CurrentActor actor = ExpenseFixture.Actor("other", AppRoles.Employee, extraRole);

        var update = await fixture.Service.UpdateAsync(actor, expense.Id, ExpenseFixture.Input("Changed expense"));
        var submit = await fixture.Service.SubmitAsync(actor, expense.Id);
        Assert.AreEqual(expected, update.Error!.Kind);
        Assert.AreEqual(expected, submit.Error!.Kind);
        Assert.AreEqual(status, fixture.Store.Expenses.Single().Status);
        Assert.AreEqual("Valid expense", fixture.Store.Expenses.Single().Description);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow(AppRoles.Approver)]
    [DataRow(AppRoles.Finance)]
    [DataRow(AppRoles.Auditor)]
    [DataRow(AppRoles.Admin)]
    [DataRow("Unknown")]
    [DataRow("")]
    public async Task OwnershipDoesNotGrantWriteWithoutEmployeeRole(string role)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed();
        CurrentActor actor = ExpenseFixture.Actor("owner", role.Split(',', StringSplitOptions.RemoveEmptyEntries));
        var update = await fixture.Service.UpdateAsync(actor, expense.Id, ExpenseFixture.Input("Changed expense"));
        var submit = await fixture.Service.SubmitAsync(actor, expense.Id);
        Assert.AreEqual(ServiceErrorKind.Forbidden, update.Error!.Kind);
        Assert.AreEqual(ServiceErrorKind.Forbidden, submit.Error!.Kind);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    public async Task PrincipalIdentityAndStandardRoleClaimsDetermineExpenseOwner()
    {
        var fixture = new ExpenseFixture();
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "authenticated-id"),
            new Claim(ClaimTypes.Role, AppRoles.Employee),
            new Claim(ClaimTypes.Role, AppRoles.Approver),
            new Claim("OwnerId", "untrusted-owner"),
            new Claim("Role", AppRoles.Admin),
        ], "test");
        CurrentActor actor = CurrentActor.FromPrincipal(new ClaimsPrincipal(identity));
        var created = await fixture.Service.CreateAsync(actor, ExpenseFixture.Input());
        Assert.IsTrue(created.Succeeded);
        Assert.AreEqual("authenticated-id", created.Value.OwnerId);
        Assert.AreEqual("authenticated-id", fixture.Store.Histories.Single().ActorId);
        Assert.IsTrue(actor.HasRole(AppRoles.Approver));
        Assert.IsFalse(actor.HasRole(AppRoles.Admin));
    }

    [TestMethod]
    public async Task DraftUpdatePreservesOwnershipAndCreationAndRecordsDateAndCategoryChanges()
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed();
        var result = await fixture.Service.UpdateAsync(ExpenseFixture.Actor("owner", AppRoles.Employee, AppRoles.Approver), expense.Id,
            ExpenseFixture.Input(dayOffset: -1, categoryId: 2));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(expense.Id, result.Value.Id);
        Assert.AreEqual("owner", result.Value.OwnerId);
        Assert.AreEqual("Draft", result.Value.Status);
        Assert.AreEqual(expense.CreatedAtUtc, result.Value.CreatedAtUtc);
        Assert.AreEqual(new DateOnly(2026, 10, 13), result.Value.ExpenseDate);
        Assert.AreEqual(2, result.Value.CategoryId);
        ExpenseHistory history = fixture.Store.Histories.Single();
        Assert.AreEqual("ExpenseDate: 2026-10-14 -> 2026-10-13; CategoryId: 1 -> 2", history.Changes);
        Assert.AreEqual(expense.Id, history.ExpenseId);
        Assert.AreEqual("owner", history.ActorId);
        Assert.AreEqual(ExpenseFixture.UtcNow, history.OccurredAtUtc);
        Assert.AreEqual(1, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow("Too short", "25", 0, 1)]
    [DataRow("Valid expense", "0.009", 0, 1)]
    [DataRow("Valid expense", "2147483647.01", 0, 1)]
    [DataRow("Valid expense", "25", 1, 1)]
    [DataRow("Valid expense", "25", 0, 99)]
    public async Task InvalidDraftUpdatePreservesPersistedValues(string description, string amount, int offset, int category)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed();
        var result = await fixture.Service.UpdateAsync(ExpenseFixture.Actor("owner", AppRoles.Employee), expense.Id,
            ExpenseFixture.Input(description, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), offset, category));
        Assert.AreEqual(ServiceErrorKind.Validation, result.Error!.Kind);
        Expense saved = fixture.Store.Expenses.Single();
        Assert.AreEqual("Valid expense", saved.Description);
        Assert.AreEqual(25m, saved.Amount);
        Assert.AreEqual(expense.ConcurrencyStamp, saved.ConcurrencyStamp);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    public async Task UpdateSaveConflictPreservesExpenseAndDoesNotLeakHistoryIntoNextSave()
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed();
        CurrentActor actor = ExpenseFixture.Actor("owner", AppRoles.Employee);
        fixture.Store.ConflictOnSave = true;
        var failed = await fixture.Service.UpdateAsync(actor, expense.Id, ExpenseFixture.Input("Changed expense"));
        Assert.AreEqual(ServiceErrorKind.Conflict, failed.Error!.Kind);
        Assert.AreEqual("Valid expense", fixture.Store.Expenses.Single().Description);
        Assert.AreEqual(expense.ConcurrencyStamp, fixture.Store.Expenses.Single().ConcurrencyStamp);
        Assert.IsEmpty(fixture.Store.Histories);
        fixture.Store.ConflictOnSave = false;
        var submitted = await fixture.Service.SubmitAsync(actor, expense.Id);
        Assert.IsTrue(submitted.Succeeded);
        Assert.AreEqual("Valid expense", submitted.Value.Description);
        Assert.AreEqual(ExpenseHistoryAction.Submitted, fixture.Store.Histories.Single().Action);
    }
}
