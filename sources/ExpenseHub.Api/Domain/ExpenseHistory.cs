using System;

namespace ExpenseHub.Api.Domain;

/// <summary>
/// Registro imutável de uma ação executada sobre um reembolso.
/// </summary>
internal sealed class ExpenseHistory
{
    public long Id { get; set; }

    public Guid ExpenseId { get; set; }

    public ExpenseHistoryAction Action { get; set; }

    public string ActorId { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public ExpenseStatus? FromStatus { get; set; }

    public ExpenseStatus ToStatus { get; set; }

    public string? Justification { get; set; }

    /// <summary>
    /// Gets or sets a descrição das alterações feitas em Draft (campo: antes -> depois).
    /// </summary>
    public string? Changes { get; set; }
}
