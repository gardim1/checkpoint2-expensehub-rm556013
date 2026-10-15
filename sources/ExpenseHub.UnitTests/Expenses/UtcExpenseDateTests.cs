using System;
using System.Linq;
using System.Threading.Tasks;
using ExpenseHub.Api.Common;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Dtos;
using ExpenseHub.Api.Expenses;
using ExpenseHub.Api.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Expenses;

[TestClass]
internal sealed class UtcExpenseDateTests
{
    [TestMethod]
    [DataRow(false, -1, true)]
    [DataRow(false, 0, true)]
    [DataRow(false, 1, false)]
    [DataRow(true, -1, true)]
    [DataRow(true, 0, true)]
    [DataRow(true, 1, false)]
    public async Task CreateAndUpdateUseUtcDayEvenWhenLocalCalendarHasAdvanced(bool update, int dayOffset, bool valid)
    {
        var fixture = new ExpenseFixture();
        var service = new ExpenseService(fixture.Store, new MidnightTimeProvider());
        Expense original = fixture.Seed();
        CurrentActor actor = ExpenseFixture.Actor("owner", AppRoles.Employee);
        var input = new ExpenseInput
        {
            Description = "Changed expense",
            Amount = 25m,
            CategoryId = 1,
            ExpenseDate = new DateOnly(2026, 10, 14).AddDays(dayOffset),
        };

        var result = update
            ? await service.UpdateAsync(actor, original.Id, input)
            : await service.CreateAsync(actor, input);

        Assert.AreEqual(valid, result.Succeeded);
        if (valid)
        {
            Assert.IsTrue(result.Succeeded);
            var expectedUtc = new DateTime(2026, 10, 14, 23, 59, 0, DateTimeKind.Utc);
            Assert.AreEqual(expectedUtc, result.Value.UpdatedAtUtc);
            Assert.AreEqual(input.ExpenseDate, result.Value.ExpenseDate);
            ExpenseHistory history = fixture.Store.Histories.Single();
            Assert.AreEqual(expectedUtc, history.OccurredAtUtc);
            Assert.AreEqual(DateTimeKind.Utc, history.OccurredAtUtc.Kind);
            Assert.AreEqual(1, fixture.Store.Saves);
        }
        else
        {
            Assert.AreEqual(ServiceErrorKind.Validation, result.Error!.Kind);
            Assert.HasCount(1, fixture.Store.Expenses);
            Assert.AreEqual(original.ConcurrencyStamp, fixture.Store.Expenses.Single().ConcurrencyStamp);
            Assert.IsEmpty(fixture.Store.Histories);
            Assert.AreEqual(0, fixture.Store.Saves);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingExpenseDateCannotBeCreatedOrPersistedByUpdate(bool update)
    {
        var fixture = new ExpenseFixture();
        Expense original = fixture.Seed();
        CurrentActor actor = ExpenseFixture.Actor("owner", AppRoles.Employee);
        var input = new ExpenseInput { Description = "Changed expense", Amount = 25m, CategoryId = 1 };

        var result = update
            ? await fixture.Service.UpdateAsync(actor, original.Id, input)
            : await fixture.Service.CreateAsync(actor, input);

        Assert.AreEqual(ServiceErrorKind.Validation, result.Error!.Kind);
        Assert.AreEqual(original.ExpenseDate, fixture.Store.Expenses.Single().ExpenseDate);
        Assert.IsEmpty(fixture.Store.Histories);
        Assert.AreEqual(0, fixture.Store.Saves);
    }

    private sealed class MidnightTimeProvider : TimeProvider
    {
        private static readonly TimeZoneInfo _localZone = TimeZoneInfo.CreateCustomTimeZone(
            "I09-local", TimeSpan.FromHours(3), "I09-local", "I09-local");

        public override TimeZoneInfo LocalTimeZone => _localZone;

        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 23, 59, 0, TimeSpan.Zero);
    }
}
