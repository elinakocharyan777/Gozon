using System.Text;
using System.Text.Json;
using Gozon.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Payments.Api.Db;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Payments.Api.Messaging;

public sealed class PaymentRequestsConsumerHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly RabbitMqBus _bus;
    private readonly ILogger<PaymentRequestsConsumerHostedService> _logger;

    public PaymentRequestsConsumerHostedService(IServiceProvider sp, RabbitMqBus bus, ILogger<PaymentRequestsConsumerHostedService> logger)
    {
        _sp = sp;
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var channel = _bus.CreateChannel();
        channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

        channel.QueueDeclare(queue: Queues.PaymentRequests, durable: true, exclusive: false, autoDelete: false, arguments: null);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            try
            {
                var type = ea.BasicProperties?.Type ?? "";
                if (type != nameof(OrderPaymentRequested))
                {
                    _logger.LogWarning("Unknown message type '{Type}'. Ack and skip.", type);
                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                    return;
                }

                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                var evt = JsonSerializer.Deserialize<OrderPaymentRequested>(json)!;

                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

                await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);

                // Transactional Inbox
                var inboxExists = await db.Inbox.AnyAsync(x => x.MessageId == evt.MessageId, stoppingToken);
                if (inboxExists)
                {
                    await tx.CommitAsync(stoppingToken);
                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                    return;
                }

                db.Inbox.Add(new InboxMessageEntity { MessageId = evt.MessageId, ReceivedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync(stoppingToken);

                var existing = await db.Operations.FirstOrDefaultAsync(x => x.OrderId == evt.OrderId, stoppingToken);
                if (existing != null)
                {
                    await EnqueueResultFromExisting(existing, evt, db, stoppingToken);
                    await tx.CommitAsync(stoppingToken);
                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                    return;
                }

                // делает попытку оплаты
                var (status, paymentId, reason) = await TryDebitAsync(db, evt.UserId, evt.Amount, stoppingToken);

                var op = new PaymentOperationEntity
                {
                    Id = Guid.NewGuid(),
                    OrderId = evt.OrderId,
                    UserId = evt.UserId,
                    Amount = evt.Amount,
                    Status = status,
                    PaymentId = paymentId,
                    Reason = reason,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                db.Operations.Add(op);

                try
                {
                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
                {
                    // ƒруга€ служба доставки (или параллельный рабочий) вставила операцию. 
                    db.ChangeTracker.Clear();
                    var already = await db.Operations.FirstAsync(x => x.OrderId == evt.OrderId, stoppingToken);
                    await EnqueueResultFromExisting(already, evt, db, stoppingToken);
                    await db.SaveChangesAsync(stoppingToken);
                    await tx.CommitAsync(stoppingToken);
                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                    return;
                }

                await EnqueueResultFromExisting(op, evt, db, stoppingToken);
                await db.SaveChangesAsync(stoppingToken);

                await tx.CommitAsync(stoppingToken);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle payment request. Will requeue.");
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        channel.BasicConsume(queue: Queues.PaymentRequests, autoAck: false, consumer: consumer);

        _logger.LogInformation("PaymentRequests consumer started.");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private static async Task EnqueueResultFromExisting(PaymentOperationEntity op, OrderPaymentRequested evt, PaymentsDbContext db, CancellationToken ct)
    {
        if (op.Status == PaymentOperationStatus.Succeeded)
        {
            var ok = new PaymentSucceeded(
                MessageId: Guid.NewGuid(),
                OrderId: evt.OrderId,
                UserId: evt.UserId,
                Amount: evt.Amount,
                PaymentId: op.PaymentId ?? Guid.Empty,
                OccurredAt: DateTimeOffset.UtcNow
            );

            db.Outbox.Add(new OutboxMessageEntity
            {
                Id = Guid.NewGuid(),
                Type = nameof(PaymentSucceeded),
                Destination = Queues.PaymentResults,
                Payload = JsonSerializer.Serialize(ok),
                OccurredAt = ok.OccurredAt
            });
        }
        else
        {
            var fail = new PaymentFailed(
                MessageId: Guid.NewGuid(),
                OrderId: evt.OrderId,
                UserId: evt.UserId,
                Amount: evt.Amount,
                Reason: op.Reason ?? "Payment failed",
                OccurredAt: DateTimeOffset.UtcNow
            );

            db.Outbox.Add(new OutboxMessageEntity
            {
                Id = Guid.NewGuid(),
                Type = nameof(PaymentFailed),
                Destination = Queues.PaymentResults,
                Payload = JsonSerializer.Serialize(fail),
                OccurredAt = fail.OccurredAt
            });
        }

        await Task.CompletedTask;
    }

    private static async Task<(PaymentOperationStatus status, Guid? paymentId, string? reason)> TryDebitAsync(
        PaymentsDbContext db,
        string userId,
        decimal amount,
        CancellationToken ct)
    {
        amount = decimal.Round(amount, 2);
        var account = await db.Accounts.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (account == null)
            return (PaymentOperationStatus.Failed, null, "Account not found");

        for (var attempt = 0; attempt < 10; attempt++)
        {
            account = await db.Accounts.AsNoTracking().FirstAsync(x => x.UserId == userId, ct);

            if (account.Balance < amount)
                return (PaymentOperationStatus.Failed, null, "Insufficient funds");

            var rows = await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE accounts
SET balance = balance - {amount},
    version = version + 1,
    updated_at = {DateTimeOffset.UtcNow}
WHERE user_id = {userId}
  AND version = {account.Version}
  AND balance >= {amount};
", ct);

            if (rows == 1)
            {
                return (PaymentOperationStatus.Succeeded, Guid.NewGuid(), null);
            }

        }

        return (PaymentOperationStatus.Failed, null, "Concurrent update, retry later");
    }
}
