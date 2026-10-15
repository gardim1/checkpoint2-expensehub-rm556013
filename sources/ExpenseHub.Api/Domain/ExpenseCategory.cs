namespace ExpenseHub.Api.Domain;

/// <summary>
/// Categoria de despesa (dado de referência semeado pela migration).
/// </summary>
internal sealed class ExpenseCategory
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
