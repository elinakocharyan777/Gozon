namespace Gozon.Contracts;

public record OrderPaymentRequested(
    Guid MessageId,
    Guid OrderId,
    string UserId,
    decimal Amount,
    DateTimeOffset OccurredAt
);

public record PaymentSucceeded(
    Guid MessageId,
    Guid OrderId,
    string UserId,
    decimal Amount,
    Guid PaymentId,
    DateTimeOffset OccurredAt
);

public record PaymentFailed(
    Guid MessageId,
    Guid OrderId,
    string UserId,
    decimal Amount,
    string Reason,
    DateTimeOffset OccurredAt
);
