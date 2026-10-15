using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Common;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Dtos;
using ExpenseHub.Api.Responses;
using ExpenseHub.Api.Security;

namespace ExpenseHub.Api.Expenses;

internal sealed class ExpenseService(IExpenseStore store, TimeProvider timeProvider)
{
    public async Task<ServiceResult<ExpenseResponse>> CreateAsync(CurrentActor actor, ExpenseInput input, CancellationToken cancellationToken = default)
    {
        if (!actor.HasRole(AppRoles.Employee))
        {
            return ServiceError.Forbidden("Employee role required.");
        }

        ServiceError? validation = await ValidateAsync(input, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            OwnerId = actor.UserId,
            CategoryId = input.CategoryId,
            Description = input.Description.Trim(),
            Amount = input.Amount,
            ExpenseDate = input.ExpenseDate,
            Status = ExpenseStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ConcurrencyStamp = Guid.NewGuid(),
        };
        store.Add(expense);
        store.Add(History(expense, actor, ExpenseHistoryAction.Created, null, now));
        return await SaveAsync(expense, cancellationToken);
    }

    public async Task<ServiceResult<ExpenseResponse>> UpdateAsync(CurrentActor actor, Guid id, ExpenseInput input, CancellationToken cancellationToken = default)
    {
        if (!actor.HasRole(AppRoles.Employee))
        {
            return ServiceError.Forbidden("Employee role required.");
        }

        Expense? expense = await store.FindAsync(id, cancellationToken);
        ServiceError? access = OwnerCommandError(actor, expense);
        if (access is not null)
        {
            return access;
        }

        ServiceError? validation = await ValidateAsync(input, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        Expense draft = expense!;
        List<string> changes = Changes(draft, input);
        if (changes.Count == 0)
        {
            return ServiceResult<ExpenseResponse>.Success(ExpenseResponse.FromExpense(draft));
        }

        Guid originalStamp = draft.ConcurrencyStamp;
        draft.Description = input.Description.Trim();
        draft.Amount = input.Amount;
        draft.ExpenseDate = input.ExpenseDate;
        draft.CategoryId = input.CategoryId;
        draft.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        draft.ConcurrencyStamp = Guid.NewGuid();
        store.Update(draft, originalStamp);
        ExpenseHistory history = History(draft, actor, ExpenseHistoryAction.Updated, ExpenseStatus.Draft, draft.UpdatedAtUtc);
        history.Changes = string.Join("; ", changes);
        store.Add(history);
        return await SaveAsync(draft, cancellationToken);
    }

    private async Task<ServiceError?> ValidateAsync(ExpenseInput input, CancellationToken cancellationToken)
    {
        int length = input.Description?.Trim().Length ?? 0;
        if (length is < 10 or > 500 || input.Amount is < 0.01m or > int.MaxValue ||
            input.ExpenseDate == default || input.ExpenseDate > DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
        {
            return ServiceError.Validation("Description, amount or expense date is invalid.");
        }

        return await store.CategoryExistsAsync(input.CategoryId, cancellationToken)
            ? null
            : ServiceError.Validation("Unknown expense category.");
    }

    private static ServiceError? OwnerCommandError(CurrentActor actor, Expense? expense)
    {
        if (expense is null)
        {
            return ServiceError.NotFound("Expense not found.");
        }

        if (expense.OwnerId != actor.UserId)
        {
            return ExpenseVisibility.For(actor).Compile()(expense)
                ? ServiceError.Forbidden("Only the owner can change this expense.")
                : ServiceError.NotFound("Expense not found.");
        }

        return expense.Status == ExpenseStatus.Draft ? null : ServiceError.Conflict("Expense must be Draft.");
    }

    private static List<string> Changes(Expense expense, ExpenseInput input)
    {
        List<string> changes = [];
        if (expense.Description != input.Description.Trim())
        {
            changes.Add($"Description: {expense.Description} -> {input.Description.Trim()}");
        }

        if (expense.Amount != input.Amount)
        {
            changes.Add(string.Create(CultureInfo.InvariantCulture, $"Amount: {expense.Amount} -> {input.Amount}"));
        }

        if (expense.ExpenseDate != input.ExpenseDate)
        {
            changes.Add(string.Create(CultureInfo.InvariantCulture, $"ExpenseDate: {expense.ExpenseDate:yyyy-MM-dd} -> {input.ExpenseDate:yyyy-MM-dd}"));
        }

        if (expense.CategoryId != input.CategoryId)
        {
            changes.Add(string.Create(CultureInfo.InvariantCulture, $"CategoryId: {expense.CategoryId} -> {input.CategoryId}"));
        }

        return changes;
    }

    private static ExpenseHistory History(Expense expense, CurrentActor actor, ExpenseHistoryAction action, ExpenseStatus? fromStatus, DateTime now) => new()
    {
        ExpenseId = expense.Id,
        Action = action,
        ActorId = actor.UserId,
        OccurredAtUtc = now,
        FromStatus = fromStatus,
        ToStatus = expense.Status,
    };

    private async Task<ServiceResult<ExpenseResponse>> SaveAsync(Expense expense, CancellationToken cancellationToken) =>
        await store.SaveChangesAsync(cancellationToken)
            ? ServiceResult<ExpenseResponse>.Success(ExpenseResponse.FromExpense(expense))
            : ServiceError.Conflict("Expense changed concurrently or payment already exists.");
}
