using System;
using System.Threading.Tasks;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExpenseHub.Worker;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddDbContext<ExpenseHubDbContext>(options => options.UseSqlite(builder.Configuration.GetConnectionString("ExpenseHub")));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IReportStore, EfReportStore>();
        builder.Services.AddScoped<ReportProcessor>();
        builder.Services.AddHostedService<ReportConsumer>();
        await builder.Build().RunAsync();
    }
}
