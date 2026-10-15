using System;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Responses;

internal sealed record ExpenseHistoryResponse(
    long Id, Guid ExpenseId, string Action, string ActorId, DateTime OccurredAtUtc,
    string? FromStatus, string ToStatus, string? Justification, string? Changes)
{
    public static ExpenseHistoryResponse FromHistory(ExpenseHistory history) => new(
        history.Id, history.ExpenseId, history.Action.ToString(), history.ActorId, history.OccurredAtUtc,
        history.FromStatus?.ToString(), history.ToStatus.ToString(), history.Justification, history.Changes);
}
