using System;
using System.Collections.Generic;
using System.Linq;
using ExpenseHub.Api.Common;
using ExpenseHub.Api.Security;

namespace ExpenseHub.Api.Admin;

internal static class RoleChangePolicy
{
    public static ServiceResult<IReadOnlyList<string>> Evaluate(
        string targetUserId,
        string actorUserId,
        IReadOnlyList<string>? requestedRoles)
    {
        if (requestedRoles is null)
        {
            return ServiceError.Validation("A lista de roles é obrigatória.");
        }

        List<string> normalized = [];
        foreach (string requestedRole in requestedRoles)
        {
            string? canonicalRole = AppRoles.All.FirstOrDefault(role =>
                string.Equals(role, requestedRole, StringComparison.OrdinalIgnoreCase));
            if (canonicalRole is null)
            {
                return ServiceError.Validation("A lista contém uma role desconhecida.");
            }

            if (!normalized.Contains(canonicalRole, StringComparer.Ordinal))
            {
                normalized.Add(canonicalRole);
            }
        }

        if (string.Equals(targetUserId, actorUserId, StringComparison.Ordinal)
            && !normalized.Contains(AppRoles.Admin, StringComparer.Ordinal))
        {
            return ServiceError.Validation("O administrador não pode remover sua própria role Admin.");
        }

        return ServiceResult<IReadOnlyList<string>>.Success(normalized);
    }
}
