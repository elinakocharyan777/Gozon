using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Payments.Api.Messaging;

public sealed class RabbitMqBus : IDisposable
{
    private readonly IConnection _connection;

    public RabbitMqBus(RabbitOptions options, ILogger<RabbitMqBus> logger)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.Host,
            UserName = options.Username,
            Password = options.Password,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
        };

        const int maxAttempts = 30;
        Exception? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                _connection = factory.CreateConnection("payments-service");
                logger.LogInformation("RabbitMQ connected to {Host}", options.Host);
                return;
            }
            catch (BrokerUnreachableException ex)
            {
                last = ex;
                var delayMs = Math.Min(5000, 200 * (int)Math.Pow(2, attempt - 1));
                logger.LogWarning(ex, "RabbitMQ not reachable (attempt {Attempt}/{Max}). Retrying in {Delay}ms...", attempt, maxAttempts, delayMs);
                Thread.Sleep(delayMs);
            }
        }

        throw new InvalidOperationException("RabbitMQ not reachable after retries.", last);
    }

    public IModel CreateChannel() => _connection.CreateModel();

    public void Dispose() => _connection.Dispose();
}
