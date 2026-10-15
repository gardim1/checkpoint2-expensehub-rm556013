using System;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Responses;

internal sealed record ExpenseResponse(
    Guid Id, string OwnerId, int CategoryId, string Description, decimal Amount,
    DateOnly ExpenseDate, string Status, DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    DateTime? SubmittedAtUtc, string? DecidedById, DateTime? DecidedAtUtc, string? RejectionReason,
    PaymentResponse? Payment)
{
    public static ExpenseResponse FromExpense(Expense expense) => new(
        expense.Id, expense.OwnerId, expense.CategoryId, expense.Description, expense.Amount,
        expense.ExpenseDate, expense.Status.ToString(), expense.CreatedAtUtc, expense.UpdatedAtUtc,
        expense.SubmittedAtUtc, expense.DecidedById, expense.DecidedAtUtc, expense.RejectionReason,
        expense.Payment is { } payment ? new PaymentResponse(payment.Id, payment.ExpenseId, payment.Amount, payment.PaidById, payment.PaidAtUtc) : null);
}
