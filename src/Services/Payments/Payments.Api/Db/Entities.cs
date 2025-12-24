namespace Payments.Api.Db;

public sealed class AccountEntity
{
    public string UserId { get; set; } = default!;
    public decimal Balance { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum PaymentOperationStatus
{
    Succeeded = 0,
    Failed = 1
}

public sealed class PaymentOperationEntity
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string UserId { get; set; } = default!;
    public decimal Amount { get; set; }
    public PaymentOperationStatus Status { get; set; }
    public Guid? PaymentId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
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
