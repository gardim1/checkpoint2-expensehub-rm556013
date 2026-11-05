using System;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace ExpenseHub.Api.Reports;

internal static class RabbitSettings
{
    public static string Queue(IConfiguration configuration) => configuration["RabbitMQ:Queue"] ?? "expensehub.reports";

    public static ConnectionFactory Factory(IConfiguration configuration) => new()
    {
        HostName = configuration["RabbitMQ:Host"] ?? "localhost",
        UserName = configuration["RabbitMQ:User"] ?? throw new InvalidOperationException("Configure RabbitMQ:User."),
        Password = configuration["RabbitMQ:Password"] ?? throw new InvalidOperationException("Configure RabbitMQ:Password."),
        RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
        AutomaticRecoveryEnabled = true,
    };
}

internal sealed record ReportMessage([property: JsonPropertyName("reportId")] Guid ReportId);
