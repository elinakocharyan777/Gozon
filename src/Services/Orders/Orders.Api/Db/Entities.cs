using Gozon.Contracts;

namespace Orders.Api.Db;

public sealed class OrderEntity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public decimal Amount { get; set; }
    public OrderStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class InboxMessageEntity
{
    public Guid MessageId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class OutboxMessageEntity
{
    public Guid Id { get; set; }
    public string Type { get; set; } = default!;
    public string Destination { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
