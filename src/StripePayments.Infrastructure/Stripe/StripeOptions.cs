namespace StripePayments.Infrastructure.Stripe;

public sealed class StripeOptions
{
    public const string Section = "Stripe";

    /// <summary>Test-mode secret key (sk_test_... or rk_test_...). Never commit it.</summary>
    public string? SecretKey { get; init; }

    /// <summary>Webhook signing secret (whsec_...).</summary>
    public string? WebhookSecret { get; init; }

    /// <summary>
    /// Optional file containing the signing secret. docker-compose's stripe-cli container writes
    /// the `stripe listen` secret here so no manual copy/paste is needed.
    /// </summary>
    public string? WebhookSecretFile { get; init; }

    public static bool IsTestKey(string key) =>
        key.StartsWith("sk_test_", StringComparison.Ordinal) || key.StartsWith("rk_test_", StringComparison.Ordinal);
}
