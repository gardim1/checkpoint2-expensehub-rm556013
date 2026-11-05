using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ExpenseHub.Api.Reports;

internal sealed class ReportProcessor(IReportStore store, TimeProvider clock, ILogger<ReportProcessor> logger)
{
    public async Task ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        ReportRequest? report = await store.FindAsync(id, cancellationToken);
        if (report is null)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("Report {ReportId} not found", id);
            }

            return;
        }

        if (report.Status is "Completed" or "Failed")
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Report {ReportId} duplicate ignored ({Status})", id, report.Status);
            }

            return;
        }

        report.Status = "Processing";
        report.StartedAt ??= clock.GetUtcNow().UtcDateTime;
        await store.SaveAsync(cancellationToken);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Report {ReportId} processing started", id);
        }

        try
        {
            if (report.Scope is not ("Mine" or "Global") || report.FromDate > report.ToDate || string.IsNullOrWhiteSpace(report.OwnerId))
            {
                throw new InvalidDataException("Invalid persisted report.");
            }

            var expenses = await store.ExpensesAsync(report, cancellationToken);
            ReportResult result = ReportCalculator.Calculate(report, expenses, clock.GetUtcNow().UtcDateTime);
            report.ResultJson = JsonSerializer.Serialize(result);
            report.Status = "Completed";
            report.CompletedAt = result.GeneratedAt;
            report.ErrorCode = null;
            report.ErrorMessage = null;
        }
        catch (InvalidDataException)
        {
            report.Status = "Failed";
            report.ResultJson = null;
            report.CompletedAt = clock.GetUtcNow().UtcDateTime;
            report.ErrorCode = "invalid_report";
            report.ErrorMessage = "Os dados registrados para o relatório são inválidos.";
        }

        // Result and terminal state are written together in one EF SaveChanges transaction.
        // Storage failures propagate: the consumer must not acknowledge them.
        await store.SaveAsync(cancellationToken);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Report {ReportId} persisted ({Status})", id, report.Status);
        }
    }
}
