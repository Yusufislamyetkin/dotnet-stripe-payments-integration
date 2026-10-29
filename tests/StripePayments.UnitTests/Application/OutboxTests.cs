using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StripePayments.Application.Outbox;
using StripePayments.Application.Webhooks;
using StripePayments.Domain.Catalog;
using StripePayments.Domain.Orders;
using StripePayments.Domain.Outbox;
using StripePayments.Infrastructure.Persistence;
using StripePayments.Tests.Shared;
using StripePayments.UnitTests.TestSupport;

namespace StripePayments.UnitTests.Application;

public class OutboxTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));
    private readonly FakePublisher _publisher = new();
    private static readonly OutboxOptions Options = new() { WebhookUrl = "https://hook.test", MaxAttempts = 4, BaseDelaySeconds = 5, MaxDelaySeconds = 3600 };

    private OutboxProcessor Processor(AppDbContext db) =>
        new(db, _publisher, Microsoft.Extensions.Options.Options.Create(Options), _clock, NullLogger<OutboxProcessor>.Instance);

    private async Task<(AppDbContext, OutboxMessage)> SeedMessage()
    {
        var db = TestDb.Create();
        var message = OutboxMessage.Create(Guid.NewGuid(), "order.paid", "{}", _clock.GetUtcNow());
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return (db, message);
    }

    [Fact]
    public async Task Successful_delivery_marks_message_delivered()
    {
        var (db, message) = await SeedMessage();

        Assert.Equal(1, await Processor(db).ProcessDueAsync(default));

        Assert.NotNull(message.DeliveredAt);
        Assert.Equal(1, message.Attempts);
        Assert.Equal(0, await Processor(db).ProcessDueAsync(default)); // not sent twice
    }

    [Fact]
    public async Task Failures_back_off_exponentially_then_dead_letter()
    {
        var (db, message) = await SeedMessage();
        _publisher.FailWith = "HTTP 500";
        var start = _clock.GetUtcNow();

        await Processor(db).ProcessDueAsync(default);
        Assert.Equal(start.AddSeconds(5), message.NextAttemptAt);

        await Processor(db).ProcessDueAsync(default); // not due yet
        Assert.Equal(1, message.Attempts);

        _clock.Advance(TimeSpan.FromSeconds(5));
        await Processor(db).ProcessDueAsync(default);
        Assert.Equal(_clock.GetUtcNow().AddSeconds(10), message.NextAttemptAt);

        _clock.Advance(TimeSpan.FromSeconds(10));
        await Processor(db).ProcessDueAsync(default);
        Assert.Equal(_clock.GetUtcNow().AddSeconds(20), message.NextAttemptAt);

        _clock.Advance(TimeSpan.FromSeconds(20));
        await Processor(db).ProcessDueAsync(default);
        Assert.NotNull(message.DeadLetteredAt);
        Assert.Equal(4, message.Attempts);
        Assert.Equal("HTTP 500", message.LastError);
        Assert.Equal(4, _publisher.Calls);
    }

    [Fact]
    public async Task Publisher_exception_is_treated_as_a_failed_attempt()
    {
        var (db, message) = await SeedMessage();
        _publisher.Throw = true;

        await Processor(db).ProcessDueAsync(default);

        Assert.Null(message.DeliveredAt);
        Assert.Equal("boom", message.LastError);
    }

    [Fact]
    public async Task Nothing_is_sent_when_no_webhook_url_is_configured()
    {
        var (db, message) = await SeedMessage();
        _publisher.Configured = false;

        await Processor(db).ProcessDueAsync(default);

        Assert.Equal(0, _publisher.Calls);
        Assert.True(message.IsPending);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(5, 80)]
    [InlineData(20, 3600)]
    public void Retry_delay_is_capped(int attempt, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryPolicy.Delay(attempt, TimeSpan.FromSeconds(5), TimeSpan.FromHours(1)));

    [Fact]
    public async Task Paid_webhook_writes_exactly_one_outbox_message_in_the_same_save()
    {
        await using var db = TestDb.Create();
        var order = Order.CreatePending(Product.Create("sku", "Item", "", 5000, "usd"), null, null, _clock.GetUtcNow());
        order.AttachCheckoutSession("cs_1");
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var webhooks = new WebhookProcessor(db, _clock, NullLogger<WebhookProcessor>.Instance);
        StripeEventEnvelope Completed(string id) => new(id, StripeEventTypes.CheckoutSessionCompleted, _clock.GetUtcNow(),
            new CheckoutSessionCompleted("cs_1", "payment", "paid", "pi_1", null, null, "a@b.com", 5000, new Dictionary<string, string>()));

        await webhooks.ProcessAsync(Completed("evt_1"), default);
        await webhooks.ProcessAsync(Completed("evt_1"), default); // replay
        await webhooks.ProcessAsync(Completed("evt_2"), default); // different event, same session

        var message = await db.OutboxMessages.SingleAsync();
        Assert.Equal(IntegrationEvents.OrderPaid, message.Type);
        using var json = JsonDocument.Parse(message.Payload);
        Assert.Equal(message.Id, json.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(order.Id, json.RootElement.GetProperty("data").GetProperty("orderId").GetGuid());
        Assert.Equal(5000, json.RootElement.GetProperty("data").GetProperty("amount").GetInt64());
    }

    [Fact]
    public void Signature_round_trips_and_rejects_tampering_and_replay()
    {
        var now = _clock.GetUtcNow();
        var ts = now.ToUnixTimeSeconds();
        var sig = OutboxSignature.Compute("secret", ts, "{\"a\":1}");

        Assert.StartsWith("sha256=", sig);
        Assert.True(OutboxSignature.Verify("secret", ts.ToString(), "{\"a\":1}", sig, now, TimeSpan.FromMinutes(5)));
        Assert.False(OutboxSignature.Verify("secret", ts.ToString(), "{\"a\":2}", sig, now, TimeSpan.FromMinutes(5)));
        Assert.False(OutboxSignature.Verify("other", ts.ToString(), "{\"a\":1}", sig, now, TimeSpan.FromMinutes(5)));
        Assert.False(OutboxSignature.Verify("secret", ts.ToString(), "{\"a\":1}", sig, now.AddMinutes(10), TimeSpan.FromMinutes(5)));
    }

    private sealed class FakePublisher : IOutboxPublisher
    {
        public bool Configured { get; set; } = true;
        public string? FailWith { get; set; }
        public bool Throw { get; set; }
        public int Calls { get; private set; }

        public bool IsConfigured => Configured;

        public Task<string?> PublishAsync(OutboxMessage message, CancellationToken ct)
        {
            Calls++;
            if (Throw) throw new HttpRequestException("boom");
            return Task.FromResult(FailWith);
        }
    }
}
