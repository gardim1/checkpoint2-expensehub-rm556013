using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Auth;
using ExpenseHub.Api.Common;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Dtos;
using ExpenseHub.Api.Responses;
using ExpenseHub.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ExpenseHub.Api.Admin;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/admin/users")
            .RequireAuthorization(policy => policy.RequireRole(AppRoles.Admin));
        group.MapGet(string.Empty, ListUsersAsync);
        group.MapPut("/{id}/roles", ChangeRolesAsync).WithValidation<ChangeRolesRequest>();
        return endpoints;
    }

    private static async Task<IResult> ListUsersAsync(ExpenseHubDbContext context, CancellationToken cancellationToken)
    {
        var users = await context.Users.AsNoTracking()
            .OrderBy(user => user.Email)
            .Select(user => new { user.Id, user.Email })
            .ToListAsync(cancellationToken);
        var assignments = await (from assignment in context.UserRoles.AsNoTracking()
                                 join role in context.Roles.AsNoTracking() on assignment.RoleId equals role.Id
                                 select new { assignment.UserId, role.Name }).ToListAsync(cancellationToken);
        ILookup<string, string> rolesByUser = assignments
            .Where(assignment => assignment.Name is not null)
            .ToLookup(assignment => assignment.UserId, assignment => assignment.Name!, StringComparer.Ordinal);

        return Results.Ok(users.Select(user => new UserResponse(
            user.Id, user.Email, rolesByUser[user.Id].Order(StringComparer.Ordinal).ToArray())));
    }

    private static async Task<IResult> ChangeRolesAsync(
        string id,
        ChangeRolesRequest request,
        ClaimsPrincipal actor,
        UserManager<IdentityUser> userManager,
        ExpenseHubDbContext context,
        CancellationToken cancellationToken)
    {
        IdentityUser? user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return ServiceError.NotFound("Usuário não encontrado.").ToProblem();
        }

        ServiceResult<IReadOnlyList<string>> decision = RoleChangePolicy.Evaluate(
            id, actor.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, request.Roles);
        if (!decision.Succeeded)
        {
            return decision.Error.ToProblem();
        }

        IList<string> currentRoles = await userManager.GetRolesAsync(user);
        string[] removedRoles = currentRoles.Except(decision.Value, StringComparer.OrdinalIgnoreCase).ToArray();
        string[] addedRoles = decision.Value.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToArray();

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (removedRoles.Length > 0)
        {
            IdentityResult removed = await userManager.RemoveFromRolesAsync(user, removedRoles);
            if (!removed.Succeeded)
            {
                return removed.ToValidationProblem();
            }
        }

        if (addedRoles.Length > 0)
        {
            IdentityResult added = await userManager.AddToRolesAsync(user, addedRoles);
            if (!added.Succeeded)
            {
                return added.ToValidationProblem();
            }
        }

        IdentityResult stamp = await userManager.UpdateSecurityStampAsync(user);
        if (!stamp.Succeeded)
        {
            return stamp.ToValidationProblem();
        }

        await transaction.CommitAsync(cancellationToken);
        // Roles são claims do token emitido: é necessário um novo login após alterá-las.
        IList<string> updatedRoles = await userManager.GetRolesAsync(user);
        return Results.Ok(new UserResponse(user.Id, user.Email, updatedRoles.Order(StringComparer.Ordinal).ToArray()));
    }
}
