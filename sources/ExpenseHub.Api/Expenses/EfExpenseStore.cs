using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Api.Expenses;

internal sealed class EfExpenseStore(ExpenseHubDbContext context) : IExpenseStore
{
    public Task<Expense?> FindAsync(Guid id, CancellationToken cancellationToken, Expression<Func<Expense, bool>>? predicate = null)
    {
        IQueryable<Expense> query = context.Expenses.AsNoTracking().Where(expense => expense.Id == id);
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return query.SingleOrDefaultAsync(cancellationToken);
    }

    public Task<List<Expense>> ListAsync(Expression<Func<Expense, bool>> predicate, CancellationToken cancellationToken) =>
        context.Expenses.AsNoTracking().Where(predicate).OrderBy(expense => expense.CreatedAtUtc).ThenBy(expense => expense.Id).ToListAsync(cancellationToken);

    public Task<List<ExpenseCategory>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        context.ExpenseCategories.AsNoTracking().OrderBy(category => category.Id).ToListAsync(cancellationToken);

    public Task<bool> CategoryExistsAsync(int id, CancellationToken cancellationToken) =>
        context.ExpenseCategories.AnyAsync(category => category.Id == id, cancellationToken);

    public Task<List<ExpenseHistory>> GetHistoryAsync(Guid expenseId, CancellationToken cancellationToken) =>
        context.ExpenseHistories.AsNoTracking().Where(history => history.ExpenseId == expenseId).OrderBy(history => history.Id).ToListAsync(cancellationToken);

    public void Add(Expense expense) => context.Expenses.Add(expense);

    public void Update(Expense expense, Guid originalStamp)
    {
        context.Expenses.Update(expense);
        context.Entry(expense).Property(item => item.ConcurrencyStamp).OriginalValue = originalStamp;
    }

    public void Add(ExpenseHistory history) => context.ExpenseHistories.Add(history);

    public void Add(PaymentRecord payment) => context.PaymentRecords.Add(payment);

    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 or 1555 })
        {
            return false;
        }
        finally
        {
            // Failed writes cannot leak tracked mutations into a later operation in this scope.
            context.ChangeTracker.Clear();
        }
    }
}
