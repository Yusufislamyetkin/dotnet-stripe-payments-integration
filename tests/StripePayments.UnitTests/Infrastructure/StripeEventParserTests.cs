using Microsoft.Extensions.Options;
using StripePayments.Application.Common;
using StripePayments.Application.Webhooks;
using StripePayments.Infrastructure.Stripe;
using StripePayments.Tests.Shared;

namespace StripePayments.UnitTests.Infrastructure;

public class StripeEventParserTests
{
    private static StripeEventParser Parser(string? secret = StripeTestEvents.WebhookSecret) =>
        new(Options.Create(new StripeOptions { WebhookSecret = secret }));

    private static readonly string Payload = StripeTestEvents.Event("evt_1", StripeEventTypes.CheckoutSessionCompleted,
        StripeTestEvents.CheckoutSession("cs_1", "6f9619ff-8b86-d011-b42d-00cf4fc964ff"));

    [Fact]
    public void Valid_signature_is_parsed_and_mapped()
    {
        var evt = Parser().Parse(Payload, StripeTestEvents.Sign(Payload));

        Assert.Equal("evt_1", evt.Id);
        var data = Assert.IsType<CheckoutSessionCompleted>(evt.Data);
        Assert.Equal("cs_1", data.SessionId);
        Assert.True(data.IsPaid);
        Assert.Equal(4900, data.AmountTotal);
        Assert.Equal("buyer@example.com", data.CustomerEmail);
    }

    [Fact]
    public void Wrong_secret_is_rejected() =>
        Assert.Throws<InvalidWebhookSignatureException>(() =>
            Parser().Parse(Payload, StripeTestEvents.Sign(Payload, "whsec_other")));

    [Fact]
    public void Tampered_payload_is_rejected() =>
        Assert.Throws<InvalidWebhookSignatureException>(() =>
            Parser().Parse(Payload.Replace("4900", "1"), StripeTestEvents.Sign(Payload)));

    [Fact]
    public void Old_timestamp_is_rejected_to_prevent_replay() =>
        Assert.Throws<InvalidWebhookSignatureException>(() =>
            Parser().Parse(Payload, StripeTestEvents.Sign(Payload, at: DateTimeOffset.UtcNow.AddMinutes(-10))));

    [Fact]
    public void Missing_header_is_rejected() =>
        Assert.Throws<InvalidWebhookSignatureException>(() => Parser().Parse(Payload, null));

    [Fact]
    public void Missing_secret_gives_clear_error() =>
        Assert.Throws<StripeNotConfiguredException>(() => Parser(secret: null).Parse(Payload, StripeTestEvents.Sign(Payload)));
}
