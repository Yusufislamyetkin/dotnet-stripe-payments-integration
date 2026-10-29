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
}

public sealed record PriceSpec(string LookupKey, string ProductName, long UnitAmount, string Currency, string? RecurringInterval = null);

public enum CheckoutMode { Payment, Subscription }

public sealed record CheckoutSessionSpec(
    CheckoutMode Mode,
    string PriceId,
    string? CustomerEmail,
    string SuccessUrl,
    string CancelUrl,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record CheckoutSessionResult(string SessionId, string Url);
