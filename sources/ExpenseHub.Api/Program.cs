using System.Threading.Tasks;
using ExpenseHub.Api.Admin;
using ExpenseHub.Api.Auth;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Expenses;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExpenseHub.Api;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails();
        builder.Services.AddDbContext<ExpenseHubDbContext>(options =>
            options.UseSqlite(builder.Configuration.GetConnectionString("ExpenseHub")));
        builder.Services.AddAuthentication(IdentityConstants.BearerScheme)
            .AddBearerToken(IdentityConstants.BearerScheme);
        builder.Services.AddAuthorization();
        builder.Services.AddIdentityCore<IdentityUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ExpenseHubDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        builder.Services.AddExpenseFeatures();

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithName("GetHealth");
        app.MapAuthEndpoints();
        app.MapAdminEndpoints();
        app.MapExpenseEndpoints();

        await app.InitializeDatabaseAsync();
        await app.RunAsync();
    }
}
