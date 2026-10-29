using System.Text;
using Microsoft.Extensions.Options;
using StripePayments.Application.Outbox;
using StripePayments.Domain.Outbox;

namespace StripePayments.Infrastructure.Outbox;

/// <summary>POSTs outbox messages as signed JSON to the configured webhook URL.</summary>
public sealed class HttpOutboxPublisher(HttpClient http, IOptions<OutboxOptions> options, TimeProvider clock) : IOutboxPublisher
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.WebhookUrl);

    public async Task<string?> PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.WebhookUrl)
        {
            Content = new StringContent(message.Payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(OutboxSignature.EventIdHeader, message.Id.ToString());
        request.Headers.Add(OutboxSignature.EventTypeHeader, message.Type);
        request.Headers.Add(OutboxSignature.TimestampHeader, timestamp.ToString());
        if (!string.IsNullOrEmpty(options.Value.SigningSecret))
            request.Headers.Add(OutboxSignature.SignatureHeader, OutboxSignature.Compute(options.Value.SigningSecret, timestamp, message.Payload));

        using var response = await http.SendAsync(request, ct);
        return response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
    }
}
