using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Api.Reports;

internal sealed class EfReportStore(ExpenseHubDbContext context) : IReportStore
{
    public async Task AddAsync(ReportRequest report, CancellationToken cancellationToken)
    {
        context.Reports.Add(report);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<ReportRequest?> FindAsync(Guid id, CancellationToken cancellationToken) => context.Reports.SingleOrDefaultAsync(report => report.Id == id, cancellationToken);

    public Task<List<ReportRequest>> ListAsync(string ownerId, CancellationToken cancellationToken) => context.Reports.AsNoTracking().Where(report => report.OwnerId == ownerId).OrderByDescending(report => report.CreatedAt).ToListAsync(cancellationToken);

    public Task<List<Expense>> ExpensesAsync(ReportRequest report, CancellationToken cancellationToken) => context.Expenses.AsNoTracking().Where(expense => expense.ExpenseDate >= report.FromDate && expense.ExpenseDate <= report.ToDate && (report.Scope == "Global" || expense.OwnerId == report.OwnerId)).ToListAsync(cancellationToken);

    public async Task SaveAsync(CancellationToken cancellationToken) => await context.SaveChangesAsync(cancellationToken);
}
