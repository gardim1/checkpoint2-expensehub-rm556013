using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Reports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ExpenseHub.Worker;

internal sealed class ReportConsumer(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<ReportConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Report consumer unavailable; reconnecting in 5 seconds");
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await using IConnection connection = await RabbitSettings.Factory(configuration).CreateConnectionAsync(stoppingToken);
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        string queue = RabbitSettings.Queue(configuration);
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            Guid id;
            try
            {
                ReportMessage? message = JsonSerializer.Deserialize<ReportMessage>(delivery.Body.Span);
                id = message?.ReportId ?? Guid.Empty;
                if (id == Guid.Empty)
                {
                    throw new JsonException();
                }
            }
            catch (JsonException)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Invalid report message rejected without requeue");
                }

                await channel.BasicNackAsync(delivery.DeliveryTag, false, false, stoppingToken);
                return;
            }

            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ReportProcessor>().ProcessAsync(id, stoppingToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, false, stoppingToken);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Report {ReportId} consumption acknowledged", id);
                }
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Report {ReportId} not acknowledged; retry after persistence/transport failure", id);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                await channel.BasicNackAsync(delivery.DeliveryTag, false, true, stoppingToken);
            }
        };
        await channel.BasicConsumeAsync(queue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Report consumer ready on queue {Queue}", queue);
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
