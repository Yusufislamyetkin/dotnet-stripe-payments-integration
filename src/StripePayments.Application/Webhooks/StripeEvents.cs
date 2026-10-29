namespace StripePayments.Application.Webhooks;

/// <summary>A verified Stripe event, mapped to provider-neutral data so handlers stay unit-testable.</summary>
public sealed record StripeEventEnvelope(string Id, string Type, DateTimeOffset Created, object? Data);

public static class StripeEventTypes
{
    public const string CheckoutSessionCompleted = "checkout.session.completed";
    public const string InvoicePaid = "invoice.paid";
    public const string InvoicePaymentFailed = "invoice.payment_failed";
    public const string SubscriptionCreated = "customer.subscription.created";
    public const string SubscriptionUpdated = "customer.subscription.updated";
    public const string SubscriptionDeleted = "customer.subscription.deleted";
    public const string ChargeRefunded = "charge.refunded";
    public const string AccountUpdated = "account.updated";
}

public sealed record AccountUpdated(string AccountId, bool ChargesEnabled, bool PayoutsEnabled, bool DetailsSubmitted);

/// <param name="AmountRefunded">Cumulative refunded amount on the charge.</param>
public sealed record ChargeRefunded(string ChargeId, string? PaymentIntentId, long Amount, long AmountRefunded);

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

public sealed record SubscriptionChanged(
    string SubscriptionId,
    string CustomerId,
    string Status,
    string? PriceId,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CanceledAt);

public sealed record InvoiceEvent(string InvoiceId, string? SubscriptionId, string? CustomerId, long AmountPaid, long AmountDue);
