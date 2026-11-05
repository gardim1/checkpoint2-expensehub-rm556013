using System;
using System.Collections.Generic;
using System.Linq;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Reports;

internal static class ReportCalculator
{
    public static ReportResult Calculate(ReportRequest report, IEnumerable<Expense> expenses, DateTime generatedAt)
    {
        var eligible = expenses.Where(expense => expense.ExpenseDate >= report.FromDate && expense.ExpenseDate <= report.ToDate && (report.Scope == "Global" || expense.OwnerId == report.OwnerId)).ToList();
        var rows = Enum.GetValues<ExpenseStatus>().Select(status => new ReportStatusTotal(status.ToString(), eligible.Count(expense => expense.Status == status), eligible.Where(expense => expense.Status == status).Sum(expense => expense.Amount))).ToList();
        return new ReportResult(generatedAt, eligible.Count, eligible.Sum(expense => expense.Amount), rows);
    }
}
