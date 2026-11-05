using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Reports;

namespace ExpenseHub.UnitTests.Reports;

internal sealed class FakeReportStore : IReportStore
{
    public List<ReportRequest> Reports { get; } = [];

    public List<Expense> Expenses { get; } = [];

    public int Saves { get; private set; }

    public int ExpenseReads { get; private set; }

    public bool FailSave { get; set; }

    public Task AddAsync(ReportRequest report, CancellationToken cancellationToken)
    {
        Reports.Add(report);
        return SaveAsync(cancellationToken);
    }

    public Task<ReportRequest?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Reports.SingleOrDefault(report => report.Id == id));

    public Task<List<ReportRequest>> ListAsync(string ownerId, CancellationToken cancellationToken) => Task.FromResult(Reports.Where(report => report.OwnerId == ownerId).ToList());

    public Task<List<Expense>> ExpensesAsync(ReportRequest report, CancellationToken cancellationToken)
    {
        ExpenseReads++;
        return Task.FromResult(Expenses.ToList());
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        if (FailSave)
        {
            throw new InvalidOperationException("private database details");
        }

        Saves++;
        return Task.CompletedTask;
    }
}
