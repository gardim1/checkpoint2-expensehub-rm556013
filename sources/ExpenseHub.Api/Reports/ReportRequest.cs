using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace ExpenseHub.Api.Reports;

internal sealed class ReportRequest
{
    public Guid Id { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    public string Scope { get; set; } = "Mine";

    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }

    public string Status { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? ResultJson { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    [NotMapped]
    public ReportResult? Result => ResultJson is null ? null : JsonSerializer.Deserialize<ReportResult>(ResultJson);

    public ReportResponse ToResponse() => new(Id, Scope, FromDate, ToDate, Status, DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc), AsUtc(StartedAt), AsUtc(CompletedAt), Result, ErrorCode is null ? null : new ReportError(ErrorCode, ErrorMessage!));

    private static DateTime? AsUtc(DateTime? value) => value is DateTime instant ? DateTime.SpecifyKind(instant, DateTimeKind.Utc) : null;
}
