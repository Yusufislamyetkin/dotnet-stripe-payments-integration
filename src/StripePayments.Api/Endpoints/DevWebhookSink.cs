using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StripePayments.Application.Outbox;

namespace StripePayments.Api.Endpoints;

public sealed record ReceivedWebhook(string? EventId, string? EventType, bool SignatureValid, DateTimeOffset ReceivedAt, JsonElement Body);

/// <summary>
/// In-memory stand-in for Make.com/Zapier so the outbox can be demoed without an external account.
/// Also shows how a receiver verifies the X-Signature header.
/// </summary>
public sealed class DevWebhookSink
{
    private readonly ConcurrentQueue<ReceivedWebhook> _received = new();

    public IReadOnlyList<ReceivedWebhook> Received => _received.Reverse().ToList();

    public void Add(ReceivedWebhook webhook)
    {
        _received.Enqueue(webhook);
        while (_received.Count > 50 && _received.TryDequeue(out _)) { }
    }
}

public static class DevWebhookSinkEndpoints
{
    public static void MapDevWebhookSink(this IEndpointRouteBuilder api)
    {
        var sink = api.MapGroup("/dev/webhook-sink").WithTags("Dev");

        sink.MapPost("/", async (HttpRequest request, DevWebhookSink store, IOptions<OutboxOptions> options, TimeProvider clock, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var secret = options.Value.SigningSecret;
            var valid = !string.IsNullOrEmpty(secret) && OutboxSignature.Verify(secret,
                request.Headers[OutboxSignature.TimestampHeader], body, request.Headers[OutboxSignature.SignatureHeader],
                clock.GetUtcNow(), TimeSpan.FromMinutes(5));

            store.Add(new ReceivedWebhook(request.Headers[OutboxSignature.EventIdHeader], request.Headers[OutboxSignature.EventTypeHeader],
                valid, clock.GetUtcNow(), JsonDocument.Parse(body).RootElement.Clone()));
            return valid ? Results.Ok() : Results.Unauthorized();
        }).WithSummary("Demo receiver for outbox webhooks; rejects invalid signatures with 401");

        sink.MapGet("/", (DevWebhookSink store) => store.Received);
    }
}
