using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using StripePayments.Application.Abstractions;
using StripePayments.Application.Common;
using StripePayments.Application.Webhooks;

namespace StripePayments.Infrastructure.Stripe;

public sealed class StripeEventParser(IOptions<StripeOptions> options) : IStripeEventParser
{
    private const long ToleranceSeconds = 300;
    private string? _fileSecret;

    public StripeEventEnvelope Parse(string json, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
            throw new InvalidWebhookSignatureException("Missing Stripe-Signature header.");

        Event e;
        try
        {
            // The event's API version may differ from the SDK's pinned version (e.g. stripe-cli uses the
            // account default); only the fields mapped below are read, so a mismatch is accepted.
            e = EventUtility.ConstructEvent(json, signatureHeader, ResolveSecret(), ToleranceSeconds, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            throw new InvalidWebhookSignatureException(ex.Message);
        }

        return new StripeEventEnvelope(e.Id, e.Type, ToUtc(e.Created), Map(e));
    }

    private static object? Map(Event e) => (e.Type, e.Data.Object) switch
    {
        (StripeEventTypes.CheckoutSessionCompleted, Session s) => new CheckoutSessionCompleted(
            s.Id, s.Mode, s.PaymentStatus, s.PaymentIntentId, s.SubscriptionId, s.CustomerId,
            s.CustomerDetails?.Email ?? s.CustomerEmail, s.AmountTotal,
            s.Metadata ?? new Dictionary<string, string>()),

        (StripeEventTypes.SubscriptionCreated or StripeEventTypes.SubscriptionUpdated or StripeEventTypes.SubscriptionDeleted,
            Subscription s) => new SubscriptionChanged(
            s.Id, s.CustomerId, s.Status, s.Items?.Data.FirstOrDefault()?.Price?.Id,
            ToUtc(s.CurrentPeriodEnd), ToUtc(s.TrialEnd), s.CancelAtPeriodEnd, ToUtc(s.CanceledAt)),

        (StripeEventTypes.InvoicePaid or StripeEventTypes.InvoicePaymentFailed, Invoice i) => new InvoiceEvent(
            i.Id, i.SubscriptionId, i.CustomerId, i.AmountPaid, i.AmountDue),

        _ => null
    };

    private static DateTimeOffset ToUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToUtc(DateTime? value) => value is { } v ? ToUtc(v) : null;

    private string ResolveSecret()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.WebhookSecret))
            return options.Value.WebhookSecret;

        // The stripe-cli container may write the file after the API has started, so read lazily.
        if (_fileSecret is null && options.Value.WebhookSecretFile is { Length: > 0 } path && System.IO.File.Exists(path))
            _fileSecret = System.IO.File.ReadAllText(path).Trim() is { Length: > 0 } s ? s : null;

        return _fileSecret ?? throw new StripeNotConfiguredException(
            "Stripe webhook secret is not configured. Set Stripe__WebhookSecret (whsec_...) or run the stripe-cli service.");
    }
}
