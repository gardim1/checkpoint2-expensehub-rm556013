using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Common;
using ExpenseHub.Api.Dtos;
using ExpenseHub.Api.Responses;
using ExpenseHub.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExpenseHub.Api.Expenses;

internal static class ExpenseEndpoints
{
    public static IServiceCollection AddExpenseFeatures(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IExpenseStore, EfExpenseStore>();
        services.AddScoped<ExpenseService>();
        return services;
    }

    public static IEndpointRouteBuilder MapExpenseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/expenses", CreateAsync).WithValidation<ExpenseInput>().RequireAuthorization(policy => policy.RequireRole(AppRoles.Employee));
        endpoints.MapPut("/api/expenses/{id:guid}", UpdateAsync).WithValidation<ExpenseInput>().RequireAuthorization(policy => policy.RequireRole(AppRoles.Employee));
        endpoints.MapGet("/api/categories", CategoriesAsync).RequireAuthorization();
        endpoints.MapPost("/api/expenses/{id:guid}/submit", SubmitAsync).RequireAuthorization(policy => policy.RequireRole(AppRoles.Employee));
        endpoints.MapGet("/api/expenses", ListAsync).RequireAuthorization(policy => policy.RequireRole(AppRoles.Employee, AppRoles.Approver, AppRoles.Finance, AppRoles.Auditor));
        endpoints.MapGet("/api/expenses/{id:guid}", GetAsync).RequireAuthorization(policy => policy.RequireRole(AppRoles.Employee, AppRoles.Approver, AppRoles.Finance, AppRoles.Auditor));
        endpoints.MapPost("/api/expenses/{id:guid}/approve", ApproveAsync).RequireAuthorization(policy => policy.RequireRole(AppRoles.Approver));
        endpoints.MapPost("/api/expenses/{id:guid}/reject", RejectAsync).WithValidation<RejectExpenseInput>().RequireAuthorization(policy => policy.RequireRole(AppRoles.Approver));
        endpoints.MapPost("/api/expenses/{id:guid}/pay", PayAsync).RequireAuthorization(policy => policy.RequireRole(AppRoles.Finance));
        endpoints.MapGet("/api/expenses/{id:guid}/history", HistoryAsync).RequireAuthorization(policy => policy.RequireRole(AppRoles.Employee, AppRoles.Approver, AppRoles.Finance, AppRoles.Auditor));
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(ClaimsPrincipal user, ExpenseInput input, ExpenseService service, CancellationToken cancellationToken)
    {
        ServiceResult<ExpenseResponse> result = await service.CreateAsync(CurrentActor.FromPrincipal(user), input, cancellationToken);
        return result.Succeeded
            ? Results.Created(string.Create(CultureInfo.InvariantCulture, $"/api/expenses/{result.Value.Id}"), result.Value)
            : result.Error.ToProblem();
    }

    private static async Task<IResult> UpdateAsync(Guid id, ClaimsPrincipal user, ExpenseInput input, ExpenseService service, CancellationToken cancellationToken)
    {
        ServiceResult<ExpenseResponse> result = await service.UpdateAsync(CurrentActor.FromPrincipal(user), id, input, cancellationToken);
        return ToHttpResult(result);
    }

    private static IResult ToHttpResult<T>(ServiceResult<T> result) => result.Succeeded ? Results.Ok(result.Value) : result.Error.ToProblem();

    private static async Task<IResult> SubmitAsync(Guid id, ClaimsPrincipal user, ExpenseService service, CancellationToken cancellationToken) =>
        ToHttpResult(await service.SubmitAsync(CurrentActor.FromPrincipal(user), id, cancellationToken));

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ExpenseService service, CancellationToken cancellationToken) =>
        ToHttpResult(await service.ListAsync(CurrentActor.FromPrincipal(user), cancellationToken));

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, ExpenseService service, CancellationToken cancellationToken) =>
        ToHttpResult(await service.GetAsync(CurrentActor.FromPrincipal(user), id, cancellationToken));

    private static async Task<IResult> ApproveAsync(Guid id, ClaimsPrincipal user, ExpenseService service, CancellationToken cancellationToken) =>
        ToHttpResult(await service.ApproveAsync(CurrentActor.FromPrincipal(user), id, cancellationToken));

    private static async Task<IResult> RejectAsync(Guid id, ClaimsPrincipal user, RejectExpenseInput input, ExpenseService service, CancellationToken cancellationToken) =>
        ToHttpResult(await service.RejectAsync(CurrentActor.FromPrincipal(user), id, input, cancellationToken));

    private static async Task<IResult> PayAsync(Guid id, ClaimsPrincipal user, ExpenseService service, CancellationToken cancellationToken) =>
        ToHttpResult(await service.PayAsync(CurrentActor.FromPrincipal(user), id, cancellationToken));

    private static async Task<IResult> HistoryAsync(Guid id, ClaimsPrincipal user, ExpenseService service, CancellationToken cancellationToken) =>
        ToHttpResult(await service.GetHistoryAsync(CurrentActor.FromPrincipal(user), id, cancellationToken));

    private static async Task<IResult> CategoriesAsync(IExpenseStore store, CancellationToken cancellationToken)
    {
        var categories = await store.GetCategoriesAsync(cancellationToken);
        return Results.Ok(categories.Select(category => new CategoryResponse(category.Id, category.Name)));
    }
}
