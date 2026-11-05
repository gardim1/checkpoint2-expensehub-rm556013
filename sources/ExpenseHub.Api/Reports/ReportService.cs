using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ExpenseHub.Api.Reports;

internal sealed class ReportService(IReportStore store, IReportPublisher publisher, TimeProvider clock, ILogger<ReportService> logger)
{
    public async Task<ReportCreation> CreateAsync(CurrentActor actor, ReportInput input, CancellationToken cancellationToken)
    {
        if (input.FromDate is null || input.ToDate is null || input.FromDate > input.ToDate || input.Scope is not ("Mine" or "Global"))
        {
            return new ReportCreation(400, null, new ReportError("invalid_report", "Informe datas válidas em ordem e escopo Mine ou Global."));
        }

        if (!(input.Scope == "Mine" ? actor.HasRole(AppRoles.Employee) : actor.HasRole(AppRoles.Auditor)))
        {
            return new ReportCreation(403, null, new ReportError("scope_forbidden", "Perfil sem permissão para este escopo."));
        }

        var report = new ReportRequest
        {
            Id = Guid.NewGuid(),
            OwnerId = actor.UserId,
            Scope = input.Scope,
            FromDate = input.FromDate.Value,
            ToDate = input.ToDate.Value,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };

        try
        {
            await store.AddAsync(report, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Report registration failed");
            return new ReportCreation(503, null, new ReportError("storage_unavailable", "Não foi possível registrar a solicitação."));
        }

        try
        {
            await publisher.PublishAsync(report.Id, cancellationToken);
            if (logger.IsEnabled(LogLevel.Information)) { logger.LogInformation("Report {ReportId} publication confirmed", report.Id); }

            return new ReportCreation(202, report.ToResponse(), null);
        }
        catch (Exception exception)
        {
            // A timeout/cancellation can mean that publication happened but its confirmation was lost.
            logger.LogWarning("Report {ReportId} publication unconfirmed ({ErrorType})", report.Id, exception.GetType().Name);
            return new ReportCreation(503, report.ToResponse(), new ReportError("publication_unconfirmed", "Solicitação registrada; publicação sem confirmação. Consulte o identificador."));
        }
    }

    public async Task<List<ReportResponse>> ListAsync(CurrentActor actor, CancellationToken cancellationToken) => (await store.ListAsync(actor.UserId, cancellationToken)).Select(report => report.ToResponse()).ToList();

    public async Task<ReportResponse?> GetAsync(CurrentActor actor, Guid id, CancellationToken cancellationToken)
    {
        ReportRequest? report = await OwnAsync(actor, id, cancellationToken);
        return report?.ToResponse();
    }

    public async Task<IResult> ExcelAsync(CurrentActor actor, Guid id, CancellationToken cancellationToken)
    {
        ReportRequest? report = await OwnAsync(actor, id, cancellationToken);
        if (report is null)
        {
            return Results.NotFound();
        }

        if (report.Status != "Completed" || report.Result is not ReportResult result)
        {
            return Results.Conflict(new ReportError("report_not_completed", "Relatório ainda não concluído."));
        }

        return Results.File(ReportExcel.Create(report, result), ReportExcel.ContentType, $"report-{id}.xlsx");
    }

    private async Task<ReportRequest?> OwnAsync(CurrentActor actor, Guid id, CancellationToken cancellationToken)
    {
        ReportRequest? report = await store.FindAsync(id, cancellationToken);
        return report?.OwnerId == actor.UserId ? report : null;
    }
}

internal sealed record ReportCreation(int StatusCode, ReportResponse? Report, ReportError? Error);

