namespace StripePayments.Application.Abstractions;

/// <summary>
/// Outbound calls to Stripe. Implementations never change local state:
/// orders and subscriptions are only updated from verified webhooks.
/// </summary>
public interface IStripeGateway
{
    bool IsConfigured { get; }

    /// <summary>Returns the id of an active price with the given lookup key, creating it if missing.</summary>
    Task<string> EnsurePriceAsync(PriceSpec spec, CancellationToken ct);

    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutSessionSpec spec, CancellationToken ct);

    Task<string> CreateBillingPortalUrlAsync(string customerId, string returnUrl, CancellationToken ct);

    Task CancelSubscriptionAsync(string subscriptionId, bool atPeriodEnd, CancellationToken ct);

    /// <summary>Swaps the subscription's price with proration (upgrade or downgrade).</summary>
    Task ChangeSubscriptionPriceAsync(string subscriptionId, string newPriceId, CancellationToken ct);
}

public sealed record PriceSpec(string LookupKey, string ProductName, long UnitAmount, string Currency, string? RecurringInterval = null);

public enum CheckoutMode { Payment, Subscription }

public sealed record CheckoutSessionSpec(
    CheckoutMode Mode,
    string PriceId,
    string? CustomerEmail,
    string SuccessUrl,
    string CancelUrl,
    IReadOnlyDictionary<string, string> Metadata)
{
    public int? TrialDays { get; init; }
}

public sealed record CheckoutSessionResult(string SessionId, string Url);
