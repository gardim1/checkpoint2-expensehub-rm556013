using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;

namespace ExpenseHub.Api.Expenses;

internal interface IExpenseStore
{
    Task<Expense?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<List<Expense>> ListAsync(Expression<Func<Expense, bool>> predicate, CancellationToken cancellationToken);

    Task<List<ExpenseCategory>> GetCategoriesAsync(CancellationToken cancellationToken);

    Task<bool> CategoryExistsAsync(int id, CancellationToken cancellationToken);

    Task<List<ExpenseHistory>> GetHistoryAsync(Guid expenseId, CancellationToken cancellationToken);

    void Add(Expense expense);

    void Update(Expense expense, Guid originalStamp);

    void Add(ExpenseHistory history);

    void Add(PaymentRecord payment);

    Task<bool> SaveChangesAsync(CancellationToken cancellationToken);
}
