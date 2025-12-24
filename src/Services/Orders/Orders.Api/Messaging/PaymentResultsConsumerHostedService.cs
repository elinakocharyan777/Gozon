using System.Text;
using System.Text.Json;
using Gozon.Contracts;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Db;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Orders.Api.Messaging;

public sealed class PaymentResultsConsumerHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly RabbitMqBus _bus;
    private readonly ILogger<PaymentResultsConsumerHostedService> _logger;

    public PaymentResultsConsumerHostedService(IServiceProvider sp, RabbitMqBus bus, ILogger<PaymentResultsConsumerHostedService> logger)
    {
        _sp = sp;
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var channel = _bus.CreateChannel();
        channel.BasicQos(prefetchSize: 0, prefetchCount: 20, global: false);

        channel.QueueDeclare(queue: Queues.PaymentResults, durable: true, exclusive: false, autoDelete: false, arguments: null);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            try
            {
                var type = ea.BasicProperties?.Type ?? "";
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());

                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

                // Transactional Inbox (dedupe by messageId)
                Guid messageId = type switch
                {
                    nameof(PaymentSucceeded) => JsonSerializer.Deserialize<PaymentSucceeded>(json)!.MessageId,
                    nameof(PaymentFailed) => JsonSerializer.Deserialize<PaymentFailed>(json)!.MessageId,
                    _ => Guid.Empty
                };

                if (messageId == Guid.Empty)
                {
                    _logger.LogWarning("Unknown message type '{Type}'. Ack and skip.", type);
                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                    return;
                }

                await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);

                var inboxExists = await db.Inbox.AnyAsync(x => x.MessageId == messageId, stoppingToken);
                if (inboxExists)
                {
                    await tx.CommitAsync(stoppingToken);
                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                    return;
                }

                db.Inbox.Add(new InboxMessageEntity { MessageId = messageId, ReceivedAt = DateTimeOffset.UtcNow });

                if (type == nameof(PaymentSucceeded))
                {
                    var evt = JsonSerializer.Deserialize<PaymentSucceeded>(json)!;
                    var order = await db.Orders.FirstOrDefaultAsync(x => x.Id == evt.OrderId, stoppingToken);
                    if (order != null && order.Status == OrderStatus.PendingPayment)
                    {
                        order.Status = OrderStatus.Paid;
                        order.UpdatedAt = DateTimeOffset.UtcNow;
                    }
                }
                else if (type == nameof(PaymentFailed))
                {
                    var evt = JsonSerializer.Deserialize<PaymentFailed>(json)!;
                    var order = await db.Orders.FirstOrDefaultAsync(x => x.Id == evt.OrderId, stoppingToken);
                    if (order != null && order.Status == OrderStatus.PendingPayment)
                    {
                        order.Status = OrderStatus.PaymentFailed;
                        order.UpdatedAt = DateTimeOffset.UtcNow;
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
                await tx.CommitAsync(stoppingToken);

                channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle payment result. Will requeue.");
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        channel.BasicConsume(queue: Queues.PaymentResults, autoAck: false, consumer: consumer);

        _logger.LogInformation("PaymentResults consumer started.");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
