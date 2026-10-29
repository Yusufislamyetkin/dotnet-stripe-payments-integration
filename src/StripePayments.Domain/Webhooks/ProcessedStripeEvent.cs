namespace StripePayments.Domain.Webhooks;

/// <summary>
/// Marker row for a Stripe event that has been handled. The event id is the primary key, so a
/// replayed or concurrently delivered event can never be applied twice.
/// </summary>
public sealed class ProcessedStripeEvent
{
    private ProcessedStripeEvent() { }

    public string EventId { get; private set; } = default!;
    public string Type { get; private set; } = default!;
    public bool Handled { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

    public static ProcessedStripeEvent Create(string eventId, string type, bool handled, DateTimeOffset now) =>
        new() { EventId = eventId, Type = type, Handled = handled, ProcessedAt = now };
}
