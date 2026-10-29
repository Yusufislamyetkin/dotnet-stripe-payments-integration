namespace StripePayments.Application.Outbox;

public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    /// <summary>Receiver URL (Make.com / Zapier / n8n webhook). Messages stay pending while empty.</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>HMAC-SHA256 key used for the X-Signature header. Share it with the receiver only.</summary>
    public string? SigningSecret { get; init; }

    public int MaxAttempts { get; init; } = 8;
    public int BaseDelaySeconds { get; init; } = 5;
    public int MaxDelaySeconds { get; init; } = 3600;
    public int PollIntervalSeconds { get; init; } = 5;
    public int BatchSize { get; init; } = 20;
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>Exposes /api/dev/webhook-sink, a local receiver that verifies signatures (demo only).</summary>
    public bool EnableDevSink { get; init; }
}

public static class RetryPolicy
{
    /// <summary>Exponential backoff: base * 2^(attempt-1), capped at max. Attempt is 1-based.</summary>
    public static TimeSpan Delay(int attempt, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));
        var seconds = baseDelay.TotalSeconds * Math.Pow(2, Math.Min(attempt - 1, 30));
        return TimeSpan.FromSeconds(Math.Min(seconds, maxDelay.TotalSeconds));
    }
}
