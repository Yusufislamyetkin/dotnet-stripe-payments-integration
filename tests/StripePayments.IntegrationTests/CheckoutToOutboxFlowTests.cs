using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StripePayments.Application.Abstractions;
using StripePayments.Application.Catalog;
using StripePayments.Application.Checkout;
using StripePayments.Application.Orders;
using StripePayments.Application.Outbox;
using StripePayments.Application.Webhooks;
using StripePayments.Infrastructure.Stripe;
using StripePayments.Tests.Shared;

namespace StripePayments.IntegrationTests;

public class CheckoutToOutboxFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Checkout_then_signed_webhook_marks_order_paid_and_delivers_signed_outbox_event()
    {
        // 1. Catalog was seeded from appsettings and "synced" to (fake) Stripe on startup.
        var catalog = await _client.GetFromJsonAsync<CatalogDto>("/api/catalog");
        Assert.Contains(catalog!.Products, p => p is { Sku: "api-audit", Purchasable: true });

        // 2. Start checkout: a pending order is created.
        var checkoutResponse = await _client.PostAsJsonAsync("/api/checkout/one-time", new OneTimeCheckoutRequest("api-audit", "buyer@example.com"));
        checkoutResponse.EnsureSuccessStatusCode();
        var checkout = (await checkoutResponse.Content.ReadFromJsonAsync<CheckoutResponse>())!;
        Assert.Equal("Pending", (await GetOrder(checkout.OrderId!.Value)).Status);

        // 3. Stripe calls the webhook: signature is verified, order is marked paid.
        var payload = StripeTestEvents.Event("evt_e2e_1", StripeEventTypes.CheckoutSessionCompleted,
            StripeTestEvents.CheckoutSession(checkout.SessionId, checkout.OrderId.ToString(), amountTotal: 4410));
        Assert.Equal("Processed", await PostWebhook(payload, StripeTestEvents.Sign(payload)));

        // 4. Stripe retries the same event: nothing changes.
        Assert.Equal("Duplicate", await PostWebhook(payload, StripeTestEvents.Sign(payload)));

        var order = await GetOrder(checkout.OrderId.Value);
        Assert.Equal("Paid", order.Status);
        Assert.Equal(4410, order.Amount);

        // 5. The outbox dispatcher delivers order.paid; first attempt gets 503, the retry succeeds.
        var delivered = await WaitFor(() => factory.OutboxReceiver.Requests.FirstOrDefault(r => r.Answer == HttpStatusCode.OK));
        Assert.True(factory.OutboxReceiver.Requests.Count >= 2);
        Assert.Equal(IntegrationEvents.OrderPaid, delivered.Headers[OutboxSignature.EventTypeHeader]);
        Assert.True(OutboxSignature.Verify(ApiFactory.OutboxSecret, delivered.Headers[OutboxSignature.TimestampHeader],
            delivered.Body, delivered.Headers[OutboxSignature.SignatureHeader], DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)));

        using var body = JsonDocument.Parse(delivered.Body);
        Assert.Equal(checkout.OrderId.Value, body.RootElement.GetProperty("data").GetProperty("orderId").GetGuid());
        Assert.Equal(delivered.Headers[OutboxSignature.EventIdHeader], body.RootElement.GetProperty("id").GetString());

        var outbox = await WaitFor(async () =>
            (await _client.GetFromJsonAsync<List<OutboxMessageDto>>("/api/outbox"))!.SingleOrDefault(m => m.Status == "Delivered"));
        Assert.True(outbox.Attempts >= 2);
    }

    [Fact]
    public async Task Webhook_with_invalid_signature_is_rejected_with_problem_details()
    {
        var payload = StripeTestEvents.Event("evt_forged", StripeEventTypes.CheckoutSessionCompleted,
            StripeTestEvents.CheckoutSession("cs_forged", null));

        var response = await _client.PostAsync("/api/webhooks/stripe",
            new StringContent(payload, Encoding.UTF8, "application/json") { Headers = { { "Stripe-Signature", StripeTestEvents.Sign(payload, "whsec_wrong") } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Invalid webhook signature", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
    }

    [Fact]
    public async Task Invalid_request_returns_validation_problem()
    {
        var response = await _client.PostAsJsonAsync("/api/checkout/one-time", new OneTimeCheckoutRequest("", "not-an-email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Sku", problem!.Errors.Keys);
        Assert.Contains("Email", problem.Errors.Keys);
    }

    [Fact]
    public async Task Missing_stripe_key_returns_503_but_health_is_ok()
    {
        using var noKeys = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<IStripeGateway>(new StripeGateway(Options.Create(new StripeOptions())))));
        var client = noKeys.CreateClient();

        var response = await client.PostAsJsonAsync("/api/checkout/one-time", new OneTimeCheckoutRequest("api-audit", null));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Stripe__SecretKey", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    private async Task<OrderDto> GetOrder(Guid id) => (await _client.GetFromJsonAsync<OrderDto>($"/api/orders/{id}"))!;

    private async Task<string> PostWebhook(string payload, string signature)
    {
        var response = await _client.PostAsync("/api/webhooks/stripe",
            new StringContent(payload, Encoding.UTF8, "application/json") { Headers = { { "Stripe-Signature", signature } } });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").GetString()!;
    }

    private static Task<T> WaitFor<T>(Func<T?> probe) where T : class => WaitFor(() => Task.FromResult(probe()));

    private static async Task<T> WaitFor<T>(Func<Task<T?>> probe) where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await probe() is { } value)
                return value;
            await Task.Delay(250);
        }
        throw new TimeoutException("Condition was not met within 30 seconds.");
    }
}
