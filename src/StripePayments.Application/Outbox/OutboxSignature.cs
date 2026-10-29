using System.Security.Cryptography;
using System.Text;

namespace StripePayments.Application.Outbox;

/// <summary>
/// Signs outgoing integration webhooks: X-Signature = "sha256=" + hex(HMAC-SHA256(secret, "{timestamp}.{body}")).
/// Including the timestamp lets receivers reject replays.
/// </summary>
public static class OutboxSignature
{
    public const string SignatureHeader = "X-Signature";
    public const string TimestampHeader = "X-Timestamp";
    public const string EventIdHeader = "X-Event-Id";
    public const string EventTypeHeader = "X-Event-Type";

    public static string Compute(string secret, long timestamp, string body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool Verify(string secret, string? timestampHeader, string body, string? signatureHeader,
        DateTimeOffset now, TimeSpan tolerance)
    {
        if (signatureHeader is null || !long.TryParse(timestampHeader, out var timestamp))
            return false;
        if (Math.Abs(now.ToUnixTimeSeconds() - timestamp) > tolerance.TotalSeconds)
            return false;

        var expected = Encoding.ASCII.GetBytes(Compute(secret, timestamp, body));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(signatureHeader));
    }
}
