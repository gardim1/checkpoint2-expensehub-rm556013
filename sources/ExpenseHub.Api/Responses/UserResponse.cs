using System.Collections.Generic;

namespace ExpenseHub.Api.Responses;

internal sealed record UserResponse(string Id, string? Email, IReadOnlyList<string> Roles);
