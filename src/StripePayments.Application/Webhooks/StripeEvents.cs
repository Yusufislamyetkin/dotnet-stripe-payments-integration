namespace StripePayments.Application.Webhooks;

/// <summary>A verified Stripe event, mapped to provider-neutral data so handlers stay unit-testable.</summary>
public sealed record StripeEventEnvelope(string Id, string Type, object? Data);

public static class StripeEventTypes
{
    public const string CheckoutSessionCompleted = "checkout.session.completed";
}

public sealed record CheckoutSessionCompleted(
    string SessionId,
    string Mode,
    string PaymentStatus,
    string? PaymentIntentId,
    string? SubscriptionId,
    string? CustomerId,
    string? CustomerEmail,
    long? AmountTotal,
    IReadOnlyDictionary<string, string> Metadata)
{
    /// <summary>False for delayed payment methods that complete checkout before the money moves.</summary>
    public bool IsPaid => PaymentStatus is "paid" or "no_payment_required";
}
