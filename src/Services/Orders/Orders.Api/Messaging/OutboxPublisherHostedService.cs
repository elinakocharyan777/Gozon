using System.Text;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Db;
using RabbitMQ.Client;

namespace Orders.Api.Messaging;

public sealed class OutboxPublisherHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly RabbitMqBus _bus;
    private readonly ILogger<OutboxPublisherHostedService> _logger;

    public OutboxPublisherHostedService(IServiceProvider sp, RabbitMqBus bus, ILogger<OutboxPublisherHostedService> logger)
    {
        _sp = sp;
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var channel = _bus.CreateChannel();

        channel.QueueDeclare(queue: Queues.PaymentRequests, durable: true, exclusive: false, autoDelete: false, arguments: null);
        channel.QueueDeclare(queue: Queues.PaymentResults, durable: true, exclusive: false, autoDelete: false, arguments: null);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

                var batch = await db.Outbox
                    .Where(x => x.ProcessedAt == null)
                    .OrderBy(x => x.OccurredAt)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                if (batch.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                foreach (var msg in batch)
                {
                    try
                    {
                        var props = channel.CreateBasicProperties();
                        props.Persistent = true;
                        props.Type = msg.Type;

                        var body = Encoding.UTF8.GetBytes(msg.Payload);
                        channel.BasicPublish(exchange: "", routingKey: msg.Destination, basicProperties: props, body: body);

                        msg.ProcessedAt = DateTimeOffset.UtcNow;
                        msg.Attempts += 1;
                        msg.LastError = null;

                        _logger.LogInformation("Outbox published {Type} to {Destination} (Id={Id})", msg.Type, msg.Destination, msg.Id);
                    }
                    catch (Exception ex)
                    {
                        msg.Attempts += 1;
                        msg.LastError = ex.Message;
                        _logger.LogError(ex, "Outbox publish failed (Id={Id}, Type={Type})", msg.Id, msg.Type);
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox loop failure");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}
