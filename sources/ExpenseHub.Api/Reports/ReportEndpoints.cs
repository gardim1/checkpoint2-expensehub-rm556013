using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExpenseHub.Api.Reports;

internal static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/reports").RequireAuthorization();
        group.MapPost(string.Empty, CreateAsync);
        group.MapGet(string.Empty, async (ClaimsPrincipal user, ReportService service, CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(CurrentActor.FromPrincipal(user), cancellationToken)));
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/{id:guid}/excel", (Guid id, ClaimsPrincipal user, ReportService service, CancellationToken cancellationToken) => service.ExcelAsync(CurrentActor.FromPrincipal(user), id, cancellationToken));
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(ClaimsPrincipal user, ReportInput input, ReportService service, CancellationToken cancellationToken)
    {
        ReportCreation result = await service.CreateAsync(CurrentActor.FromPrincipal(user), input, cancellationToken);
        return result.StatusCode == 202
            ? Results.Accepted($"/api/reports/{result.Report!.Id}", result.Report)
            : Results.Json(new { id = result.Report?.Id, error = result.Error }, statusCode: result.StatusCode);
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, ReportService service, CancellationToken cancellationToken)
    {
        ReportResponse? report = await service.GetAsync(CurrentActor.FromPrincipal(user), id, cancellationToken);
        return report is null ? Results.NotFound() : Results.Ok(report);
    }
}

