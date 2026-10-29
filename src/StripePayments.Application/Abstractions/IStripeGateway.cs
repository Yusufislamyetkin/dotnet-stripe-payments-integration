using StripePayments.Application.Catalog;
using StripePayments.Application.Coupons;

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

    Task<RefundResult> CreateRefundAsync(string paymentIntentId, long amount, string? reason, string idempotencyKey, CancellationToken ct);

    /// <summary>Looks up a customer-facing promotion code (case-sensitive, as in Stripe).</summary>
    Task<PromotionCodeInfo?> FindPromotionCodeAsync(string code, CancellationToken ct);

    /// <summary>Creates the coupon and promotion code if no promotion code with this code exists yet.</summary>
    Task EnsurePromotionCodeAsync(CouponOption coupon, CancellationToken ct);
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

    /// <summary>Pre-applied promotion code. When null, Checkout shows its own promotion code field.</summary>
    public string? PromotionCodeId { get; init; }
}

public sealed record CheckoutSessionResult(string SessionId, string Url);

public sealed record RefundResult(string RefundId, string Status);
