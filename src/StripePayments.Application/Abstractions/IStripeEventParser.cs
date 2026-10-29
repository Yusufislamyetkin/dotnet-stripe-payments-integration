using StripePayments.Application.Webhooks;

namespace StripePayments.Application.Abstractions;

public interface IStripeEventParser
{
    /// <summary>Verifies the Stripe-Signature header and maps the event.</summary>
    /// <exception cref="Common.InvalidWebhookSignatureException">Signature is missing, invalid or too old.</exception>
    StripeEventEnvelope Parse(string json, string? signatureHeader);
}
