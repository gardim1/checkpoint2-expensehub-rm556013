using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseHub.Api.Data;

/// <summary>
/// Aplica as migrations pendentes na inicialização da aplicação.
/// </summary>
internal static class DatabaseInitializer
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        ExpenseHubDbContext context = scope.ServiceProvider.GetRequiredService<ExpenseHubDbContext>();
        await context.Database.MigrateAsync();
    }
}
