using System;
using System.Linq.Expressions;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Security;

namespace ExpenseHub.Api.Expenses;

internal static class ExpenseVisibility
{
    public static Expression<Func<Expense, bool>> For(CurrentActor actor)
    {
        bool employee = actor.HasRole(AppRoles.Employee);
        bool approver = actor.HasRole(AppRoles.Approver);
        bool finance = actor.HasRole(AppRoles.Finance);
        bool auditor = actor.HasRole(AppRoles.Auditor);
        string userId = actor.UserId;
        return expense => (employee && expense.OwnerId == userId) ||
            (approver && expense.Status == ExpenseStatus.Submitted) ||
            (finance && (expense.Status == ExpenseStatus.Approved || expense.Status == ExpenseStatus.Paid)) || auditor;
    }
}
