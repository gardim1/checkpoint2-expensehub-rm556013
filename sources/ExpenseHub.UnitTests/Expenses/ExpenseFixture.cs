using System;
using System.Collections.Generic;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Dtos;
using ExpenseHub.Api.Expenses;
using ExpenseHub.Api.Security;

namespace ExpenseHub.UnitTests.Expenses;

internal sealed class ExpenseFixture
{
    public static readonly DateTime UtcNow = new(2026, 10, 14, 23, 30, 0, DateTimeKind.Utc);

    public ExpenseFixture() => Service = new ExpenseService(Store, new FixedTimeProvider());

    public FakeExpenseStore Store { get; } = new();

    public ExpenseService Service { get; }

    public static CurrentActor Actor(string id = "owner", params string[] roles) => new(id, new HashSet<string>(roles, StringComparer.Ordinal));

    public static ExpenseInput Input(string? description = "Valid expense", decimal amount = 25m, int dayOffset = 0, int categoryId = 1) => new()
    {
        Description = description!,
        Amount = amount,
        ExpenseDate = DateOnly.FromDateTime(UtcNow).AddDays(dayOffset),
        CategoryId = categoryId,
    };

    public Expense Seed(ExpenseStatus status = ExpenseStatus.Draft, string ownerId = "owner")
    {
        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Description = "Valid expense",
            Amount = 25m,
            CategoryId = 1,
            ExpenseDate = DateOnly.FromDateTime(UtcNow),
            Status = status,
            CreatedAtUtc = UtcNow.AddDays(-1),
            UpdatedAtUtc = UtcNow.AddDays(-1),
            ConcurrencyStamp = Guid.NewGuid(),
        };
        Store.Expenses.Add(expense);
        return expense;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }
}
