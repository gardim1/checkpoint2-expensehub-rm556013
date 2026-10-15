using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using ExpenseHub.Api.Common;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DiscoverInternals]

namespace ExpenseHub.UnitTests.Expenses;

[TestClass]
internal sealed class DraftExpenseTests
{
    [TestMethod]
    public async Task CreateUsesAuthenticatedOwnerAndServerTimeAndCreatesHistory()
    {
        var fixture = new ExpenseFixture();
        var result = await fixture.Service.CreateAsync(ExpenseFixture.Actor("employee", AppRoles.Employee), ExpenseFixture.Input("  Valid expense  "));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("employee", result.Value.OwnerId);
        Assert.AreEqual("Draft", result.Value.Status);
        Assert.AreEqual("Valid expense", result.Value.Description);
        Assert.AreNotEqual(Guid.Empty, result.Value.Id);
        Assert.AreEqual(ExpenseFixture.UtcNow, result.Value.CreatedAtUtc);
        ExpenseHistory history = fixture.Store.Histories.Single();
        Assert.AreEqual(ExpenseHistoryAction.Created, history.Action);
        Assert.IsNull(history.FromStatus);
        Assert.AreEqual(ExpenseStatus.Draft, history.ToStatus);
        Assert.AreEqual("employee", history.ActorId);
        Assert.AreEqual(ExpenseFixture.UtcNow, history.OccurredAtUtc);
        Assert.AreEqual(1, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow(9, false)]
    [DataRow(10, true)]
    [DataRow(500, true)]
    [DataRow(501, false)]
    public async Task DescriptionMustHaveTenToFiveHundredCharactersAfterTrim(int length, bool valid)
    {
        var fixture = new ExpenseFixture();
        var result = await fixture.Service.CreateAsync(ExpenseFixture.Actor("owner", AppRoles.Employee), ExpenseFixture.Input(" " + new string('x', length) + " "));
        Assert.AreEqual(valid, result.Succeeded);
        if (!valid)
        {
            Assert.AreEqual(ServiceErrorKind.Validation, result.Error!.Kind);
            Assert.IsEmpty(fixture.Store.Expenses);
        }
    }

    [TestMethod]
    [DataRow(9, false)]
    [DataRow(10, true)]
    [DataRow(500, true)]
    [DataRow(501, false)]
    public void DeclarativeValidationUsesTrimmedDescription(int length, bool valid)
    {
        var input = ExpenseFixture.Input(" " + new string('x', length) + " ");
        bool actual = Validator.TryValidateObject(input, new ValidationContext(input), new List<ValidationResult>(), true);
        Assert.AreEqual(valid, actual);
    }

    [TestMethod]
    [DataRow("0", false)]
    [DataRow("0.009", false)]
    [DataRow("0.01", true)]
    [DataRow("2147483647", true)]
    [DataRow("2147483647.01", false)]
    public async Task AmountMustRespectDecimalLimits(string text, bool valid)
    {
        var fixture = new ExpenseFixture();
        decimal amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var result = await fixture.Service.CreateAsync(ExpenseFixture.Actor("owner", AppRoles.Employee), ExpenseFixture.Input(amount: amount));
        Assert.AreEqual(valid, result.Succeeded);
        if (!valid)
        {
            Assert.AreEqual(ServiceErrorKind.Validation, result.Error!.Kind);
        }
    }

    [TestMethod]
    [DataRow(null, 0, 1)]
    [DataRow("          ", 0, 1)]
    [DataRow("Valid expense", 1, 1)]
    [DataRow("Valid expense", 0, 99)]
    public async Task MissingDescriptionFutureUtcDateAndUnknownCategoryAreInvalid(string? description, int offset, int categoryId)
    {
        var fixture = new ExpenseFixture();
        var result = await fixture.Service.CreateAsync(ExpenseFixture.Actor("owner", AppRoles.Employee), ExpenseFixture.Input(description, dayOffset: offset, categoryId: categoryId));
        Assert.AreEqual(ServiceErrorKind.Validation, result.Error!.Kind);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    [DataRow(AppRoles.Admin)]
    [DataRow(AppRoles.Auditor)]
    [DataRow(AppRoles.Approver)]
    [DataRow(AppRoles.Finance)]
    [DataRow("Unknown")]
    public async Task CreateRequiresEmployeeInsideService(string role)
    {
        var fixture = new ExpenseFixture();
        var result = await fixture.Service.CreateAsync(ExpenseFixture.Actor("owner", role), ExpenseFixture.Input());
        Assert.AreEqual(ServiceErrorKind.Forbidden, result.Error!.Kind);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    public async Task UpdateDraftRecordsOnlyChangedFieldsAndRenewsConcurrencyStamp()
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed();
        var result = await fixture.Service.UpdateAsync(ExpenseFixture.Actor("owner", AppRoles.Employee), expense.Id, ExpenseFixture.Input("Another expense", 30m));

        Assert.IsTrue(result.Succeeded);
        ExpenseHistory history = fixture.Store.Histories.Single();
        Assert.AreEqual(ExpenseHistoryAction.Updated, history.Action);
        Assert.AreEqual("Description: Valid expense -> Another expense; Amount: 25 -> 30", history.Changes);
        Assert.AreEqual(ExpenseStatus.Draft, history.FromStatus);
        Assert.AreEqual(ExpenseStatus.Draft, history.ToStatus);
        Assert.AreNotEqual(expense.ConcurrencyStamp, fixture.Store.Expenses.Single().ConcurrencyStamp);
        Assert.AreEqual(ExpenseFixture.UtcNow, result.Value.UpdatedAtUtc);
    }

    [TestMethod]
    public async Task UpdateWithNoChangesDoesNotSaveOrCreateHistory()
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed();
        var result = await fixture.Service.UpdateAsync(ExpenseFixture.Actor("owner", AppRoles.Employee), expense.Id, ExpenseFixture.Input());
        Assert.IsTrue(result.Succeeded);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.AreEqual(0, fixture.Store.Saves);
        Assert.AreEqual(expense.UpdatedAtUtc, result.Value.UpdatedAtUtc);
    }

    [TestMethod]
    [DataRow(ExpenseStatus.Submitted)]
    [DataRow(ExpenseStatus.Approved)]
    [DataRow(ExpenseStatus.Rejected)]
    [DataRow(ExpenseStatus.Paid)]
    public async Task UpdateRequiresDraft(ExpenseStatus status)
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed(status);
        var result = await fixture.Service.UpdateAsync(ExpenseFixture.Actor("owner", AppRoles.Employee), expense.Id, ExpenseFixture.Input("Changed expense"));
        Assert.AreEqual(ServiceErrorKind.Conflict, result.Error!.Kind);
        Assert.IsEmpty(fixture.Store.Histories);
    }

    [TestMethod]
    public async Task OtherEmployeeCannotEditDraftAndMissingExpenseReturnsNotFound()
    {
        var fixture = new ExpenseFixture();
        Expense expense = fixture.Seed();
        foreach (Guid id in new[] { expense.Id, Guid.NewGuid() })
        {
            var result = await fixture.Service.UpdateAsync(ExpenseFixture.Actor("other", AppRoles.Employee), id, ExpenseFixture.Input());
            Assert.AreEqual(ServiceErrorKind.NotFound, result.Error!.Kind);
        }

        Assert.AreEqual(0, fixture.Store.Saves);
    }

    [TestMethod]
    public async Task SaveConflictDoesNotCreateExpenseOrHistory()
    {
        var fixture = new ExpenseFixture();
        fixture.Store.ConflictOnSave = true;
        var result = await fixture.Service.CreateAsync(ExpenseFixture.Actor("owner", AppRoles.Employee), ExpenseFixture.Input());
        Assert.AreEqual(ServiceErrorKind.Conflict, result.Error!.Kind);
        Assert.IsEmpty(fixture.Store.Expenses);
        Assert.IsEmpty(fixture.Store.Histories);
    }
}
