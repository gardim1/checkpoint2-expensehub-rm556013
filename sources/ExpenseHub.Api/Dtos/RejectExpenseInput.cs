using System.ComponentModel.DataAnnotations;

namespace ExpenseHub.Api.Dtos;

internal sealed class RejectExpenseInput
{
    private readonly string _justification = string.Empty;

    [Required]
    [StringLength(500, MinimumLength = 10)]
    public string Justification
    {
        get => _justification;
        init => _justification = value?.Trim() ?? string.Empty;
    }
}
