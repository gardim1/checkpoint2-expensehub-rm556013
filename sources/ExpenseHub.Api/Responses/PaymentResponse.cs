using System;

namespace ExpenseHub.Api.Responses;

internal sealed record PaymentResponse(Guid Id, Guid ExpenseId, decimal Amount, string PaidById, DateTime PaidAtUtc);
