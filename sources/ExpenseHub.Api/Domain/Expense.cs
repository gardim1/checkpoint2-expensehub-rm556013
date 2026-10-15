using System;

namespace ExpenseHub.Api.Domain;

/// <summary>
/// Solicitação de reembolso com um único valor.
/// </summary>
internal sealed class Expense
{
    public Guid Id { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    public int CategoryId { get; set; }

    public ExpenseCategory? Category { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateOnly ExpenseDate { get; set; }

    public ExpenseStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    public string? DecidedById { get; set; }

    public DateTime? DecidedAtUtc { get; set; }

    public string? RejectionReason { get; set; }

    /// <summary>
    /// Gets or sets o token de concorrência otimista, renovado a cada alteração persistida.
    /// </summary>
    public Guid ConcurrencyStamp { get; set; }

    public PaymentRecord? Payment { get; set; }
}
