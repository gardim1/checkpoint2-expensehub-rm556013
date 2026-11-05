using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace ExpenseHub.Api.Reports;

internal sealed class RabbitReportPublisher(IConfiguration configuration) : IReportPublisher
{
    public async Task PublishAsync(Guid reportId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        ConnectionFactory factory = RabbitSettings.Factory(configuration);
        await using IConnection connection = await factory.CreateConnectionAsync(timeout.Token);
        var options = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
        await using IChannel channel = await connection.CreateChannelAsync(options, timeout.Token);
        string queue = RabbitSettings.Queue(configuration);
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: timeout.Token);
        var properties = new BasicProperties { Persistent = true, ContentType = "application/json", MessageId = reportId.ToString() };
        await channel.BasicPublishAsync(string.Empty, queue, mandatory: true, basicProperties: properties, body: JsonSerializer.SerializeToUtf8Bytes(new ReportMessage(reportId)), cancellationToken: timeout.Token);
    }
}
