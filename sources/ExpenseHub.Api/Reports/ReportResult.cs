using System;
using System.Collections.Generic;

namespace ExpenseHub.Api.Reports;

internal sealed record ReportResult(DateTime GeneratedAt, int TotalCount, decimal TotalAmount, IReadOnlyList<ReportStatusTotal> ByStatus);

internal sealed record ReportStatusTotal(string Status, int Count, decimal Amount);

internal sealed record ReportError(string Code, string Message);

internal sealed record ReportResponse(Guid Id, string Scope, DateOnly FromDate, DateOnly ToDate, string Status, DateTime CreatedAt, DateTime? StartedAt, DateTime? CompletedAt, ReportResult? Result, ReportError? Error);

internal sealed record ReportInput(DateOnly? FromDate, DateOnly? ToDate, string? Scope);
