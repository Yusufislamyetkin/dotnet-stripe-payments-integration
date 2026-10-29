using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StripePayments.Application.Abstractions;
using StripePayments.Tests.Shared;
using Testcontainers.PostgreSql;

namespace StripePayments.IntegrationTests;

/// <summary>
/// Runs the real API against PostgreSQL in Docker. Only the outbound edges are faked:
/// Stripe API calls (no network, no keys) and the outbox receiver (records requests).
/// Webhooks go through real signature verification with a test signing secret.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string OutboxSecret = "outbox_test_signing_secret";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public FakeStripeGateway Stripe { get; } = new();
    public RecordingHandler OutboxReceiver { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("Stripe:WebhookSecret", StripeTestEvents.WebhookSecret);
        builder.UseSetting("Outbox:WebhookUrl", "http://receiver.test/hook");
        builder.UseSetting("Outbox:SigningSecret", OutboxSecret);
        builder.UseSetting("Outbox:PollIntervalSeconds", "1");
        builder.UseSetting("Outbox:BaseDelaySeconds", "1");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStripeGateway>(Stripe);
            services.AddHttpClient("IOutboxPublisher").ConfigurePrimaryHttpMessageHandler(() => OutboxReceiver);
        });
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

public sealed record ReceivedRequest(Dictionary<string, string> Headers, string Body, HttpStatusCode Answer);

/// <summary>Fake integration endpoint: fails the first request to exercise retries, then accepts.</summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    public ConcurrentQueue<ReceivedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var status = Requests.IsEmpty ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
        Requests.Enqueue(new ReceivedRequest(
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
            await request.Content!.ReadAsStringAsync(ct),
            status));
        return new HttpResponseMessage(status);
    }
}
