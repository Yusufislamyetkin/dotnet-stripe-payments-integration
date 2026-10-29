namespace StripePayments.Domain.Outbox;

/// <summary>
/// An integration event written in the same transaction as the state change that caused it,
/// then delivered asynchronously (at-least-once) by the outbox dispatcher.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public Guid Id { get; private set; }
    public string Type { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>Set when retries are exhausted; the message is kept for inspection and manual replay.</summary>
    public DateTimeOffset? DeadLetteredAt { get; private set; }

    public bool IsPending => DeliveredAt is null && DeadLetteredAt is null;

    public static OutboxMessage Create(Guid id, string type, string payload, DateTimeOffset now) => new()
    {
        Id = id,
        Type = type,
        Payload = payload,
        CreatedAt = now,
        NextAttemptAt = now
    };

    public void MarkDelivered(DateTimeOffset now)
    {
        Attempts++;
        DeliveredAt = now;
        LastError = null;
    }

    /// <param name="retryAt">When to try again, or <c>null</c> to dead-letter the message.</param>
    public void MarkFailed(string error, DateTimeOffset now, DateTimeOffset? retryAt)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        if (retryAt is { } next)
            NextAttemptAt = next;
        else
            DeadLetteredAt = now;
    }
}
