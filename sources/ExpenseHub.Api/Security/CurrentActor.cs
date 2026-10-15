using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace ExpenseHub.Api.Security;

internal sealed record CurrentActor(string UserId, IReadOnlySet<string> Roles)
{
    public bool HasRole(string role) => Roles.Contains(role);

    public static CurrentActor FromPrincipal(ClaimsPrincipal user) => new(
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Authenticated user has no identifier."),
        user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal));
}
