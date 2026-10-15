using System;
using System.ComponentModel.DataAnnotations;

namespace ExpenseHub.Api.Dtos;

internal sealed class ExpenseInput
{
    private readonly string _description = string.Empty;

    [Required]
    [StringLength(500, MinimumLength = 10)]
    public string Description
    {
        get => _description;
        init => _description = value?.Trim() ?? string.Empty;
    }

    [Range(typeof(decimal), "0.01", "2147483647", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal Amount { get; init; }

    [Required]
    public DateOnly ExpenseDate { get; init; }

    [Range(1, int.MaxValue)]
    public int CategoryId { get; init; }
}
