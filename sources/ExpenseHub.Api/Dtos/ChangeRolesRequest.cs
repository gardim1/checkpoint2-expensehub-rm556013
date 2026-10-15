using System.ComponentModel.DataAnnotations;

namespace ExpenseHub.Api.Dtos;

internal sealed class ChangeRolesRequest
{
    [Required]
    public string[]? Roles { get; init; }
}
