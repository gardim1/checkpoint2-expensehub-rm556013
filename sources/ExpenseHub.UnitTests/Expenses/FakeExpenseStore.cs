using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Expenses;

namespace ExpenseHub.UnitTests.Expenses;

internal sealed class FakeExpenseStore : IExpenseStore
{
    private readonly List<Expense> _pendingExpenses = [];
    private readonly List<ExpenseHistory> _pendingHistory = [];
    private readonly List<PaymentRecord> _pendingPayments = [];

    public List<Expense> Expenses { get; } = [];

    public List<ExpenseHistory> Histories { get; } = [];

    public List<PaymentRecord> Payments { get; } = [];

    public List<ExpenseCategory> Categories { get; } = [new() { Id = 1, Name = "Travel" }, new() { Id = 2, Name = "Meals" }];

    public bool ConflictOnSave { get; set; }

    public Exception? SaveException { get; set; }

    public int Saves { get; private set; }

    public List<(Guid ExpenseId, Guid OriginalStamp)> Updates { get; } = [];

    public Task<Expense?> FindAsync(Guid id, CancellationToken cancellationToken, Expression<Func<Expense, bool>>? predicate = null) =>
        Task.FromResult(Expenses.AsQueryable().Where(expense => expense.Id == id).Where(predicate ?? (expense => true)).Select(Clone).FirstOrDefault());

    public Task<List<Expense>> ListAsync(Expression<Func<Expense, bool>> predicate, CancellationToken cancellationToken) =>
        Task.FromResult(Expenses.AsQueryable().Where(predicate).Select(Clone).ToList());

    public Task<List<ExpenseCategory>> GetCategoriesAsync(CancellationToken cancellationToken) => Task.FromResult(Categories.ToList());

    public Task<bool> CategoryExistsAsync(int id, CancellationToken cancellationToken) => Task.FromResult(Categories.Any(category => category.Id == id));

    public Task<List<ExpenseHistory>> GetHistoryAsync(Guid expenseId, CancellationToken cancellationToken) =>
        Task.FromResult(Histories.Where(history => history.ExpenseId == expenseId).OrderBy(history => history.Id).ToList());

    public void Add(Expense expense) => _pendingExpenses.Add(expense);

    public void Update(Expense expense, Guid originalStamp)
    {
        Updates.Add((expense.Id, originalStamp));
        _pendingExpenses.Add(expense);
    }

    public void Add(ExpenseHistory history) => _pendingHistory.Add(history);

    public void Add(PaymentRecord payment) => _pendingPayments.Add(payment);

    public Task<bool> SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        try
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            if (ConflictOnSave)
            {
                return Task.FromResult(false);
            }

            foreach (Expense expense in _pendingExpenses)
            {
                Expenses.RemoveAll(existing => existing.Id == expense.Id);
                Expenses.Add(Clone(expense));
            }

            foreach (ExpenseHistory history in _pendingHistory)
            {
                history.Id = Histories.Count + 1;
                Histories.Add(history);
            }

            Payments.AddRange(_pendingPayments);
            return Task.FromResult(true);
        }
        finally
        {
            _pendingExpenses.Clear();
            _pendingHistory.Clear();
            _pendingPayments.Clear();
        }
    }

    private static Expense Clone(Expense expense) => JsonSerializer.Deserialize<Expense>(JsonSerializer.Serialize(expense))!;
}
