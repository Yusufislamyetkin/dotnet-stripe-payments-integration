using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StripePayments.Tests.Shared;

/// <summary>Builds Stripe-shaped event payloads and signs them the way Stripe does (t=..,v1=HMAC-SHA256).</summary>
public static class StripeTestEvents
{
    public const string WebhookSecret = "whsec_test_secret_for_unit_tests";

    public static string Event(string id, string type, object dataObject) => JsonSerializer.Serialize(new
    {
        id,
        @object = "event",
        api_version = "2024-12-18.acacia",
        created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        livemode = false,
        pending_webhooks = 1,
        request = new { id = (string?)null, idempotency_key = (string?)null },
        type,
        data = new { @object = dataObject }
    });

    public static object CheckoutSession(string sessionId, string? orderId, long amountTotal = 4900,
        string mode = "payment", string paymentStatus = "paid", string? subscription = null, string? customer = "cus_test_1") => new
    {
        id = sessionId,
        @object = "checkout.session",
        mode,
        payment_status = paymentStatus,
        status = "complete",
        payment_intent = mode == "payment" ? "pi_" + sessionId : null,
        subscription,
        customer,
        customer_details = new { email = "buyer@example.com" },
        amount_total = amountTotal,
        currency = "usd",
        metadata = orderId is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["order_id"] = orderId }
    };

    public static string Sign(string payload, string secret = WebhookSecret, DateTimeOffset? at = null)
    {
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
