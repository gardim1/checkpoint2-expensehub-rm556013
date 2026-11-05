using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Reports;

internal interface IReportStore
{
    Task AddAsync(ReportRequest report, CancellationToken cancellationToken);

    Task<ReportRequest?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<List<ReportRequest>> ListAsync(string ownerId, CancellationToken cancellationToken);

    Task<List<Expense>> ExpensesAsync(ReportRequest report, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}
