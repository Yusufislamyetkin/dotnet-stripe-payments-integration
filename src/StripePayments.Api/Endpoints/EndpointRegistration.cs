using StripePayments.Application.Abstractions;
using StripePayments.Application.Catalog;
using StripePayments.Application.Checkout;
using StripePayments.Application.Coupons;
using StripePayments.Application.Orders;
using StripePayments.Application.Subscriptions;
using StripePayments.Application.Webhooks;

namespace StripePayments.Api.Endpoints;

public static class EndpointRegistration
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/catalog", (CatalogService catalog, CancellationToken ct) => catalog.GetAsync(ct))
            .WithTags("Catalog");

        var checkout = api.MapGroup("/checkout").WithTags("Checkout");
        checkout.MapPost("/one-time", (OneTimeCheckoutRequest request, CheckoutService service, CancellationToken ct) =>
                service.CreateOneTimeAsync(request, ct))
            .Validate<OneTimeCheckoutRequest>();

        api.MapPost("/coupons/validate", (ValidateCouponRequest request, CouponService coupons, CancellationToken ct) =>
                coupons.ValidateAsync(request, ct))
            .Validate<ValidateCouponRequest>()
            .WithTags("Coupons")
            .WithSummary("Checks a promotion code against a product or plan and returns the discounted price");

        var orders = api.MapGroup("/orders").WithTags("Orders");
        orders.MapGet("/", (OrderQueries q, CancellationToken ct, int take = 50) => q.ListAsync(take, ct));
        orders.MapGet("/{id:guid}", (Guid id, OrderQueries q, CancellationToken ct) => q.GetAsync(id, ct));

        var subscriptions = api.MapGroup("/subscriptions").WithTags("Subscriptions");
        subscriptions.MapGet("/", (SubscriptionManager m, CancellationToken ct) => m.ListAsync(ct));
        subscriptions.MapPost("/checkout", (SubscriptionCheckoutRequest request, SubscriptionManager m, CancellationToken ct) =>
                m.CreateCheckoutAsync(request, ct))
            .Validate<SubscriptionCheckoutRequest>();
        subscriptions.MapPost("/{id:guid}/cancel", async (Guid id, CancelSubscriptionRequest? request, SubscriptionManager m, CancellationToken ct) =>
            {
                await m.CancelAsync(id, request ?? new CancelSubscriptionRequest(), ct);
                return Results.Accepted();
            })
            .WithSummary("Asks Stripe to cancel; local state changes when the webhook arrives");
        subscriptions.MapPost("/{id:guid}/change-plan", async (Guid id, ChangePlanRequest request, SubscriptionManager m, CancellationToken ct) =>
            {
                await m.ChangePlanAsync(id, request, ct);
                return Results.Accepted();
            })
            .Validate<ChangePlanRequest>()
            .WithSummary("Upgrade or downgrade with proration; local state changes when the webhook arrives");
        subscriptions.MapPost("/{id:guid}/portal", (Guid id, SubscriptionManager m, CancellationToken ct) =>
                m.CreatePortalLinkAsync(id, ct))
            .WithSummary("Returns a Stripe Customer Portal link");

        api.MapPost("/webhooks/stripe", HandleStripeWebhook)
            .WithTags("Webhooks")
            .WithSummary("Stripe webhook receiver (signature verified, idempotent)");

        return app;
    }

    private static async Task<IResult> HandleStripeWebhook(
        HttpRequest request, IStripeEventParser parser, WebhookProcessor processor, CancellationToken ct)
    {
        // Signature verification needs the exact raw body, so it is read as a string, not model-bound.
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync(ct);

        var evt = parser.Parse(json, request.Headers["Stripe-Signature"]);
        var result = await processor.ProcessAsync(evt, ct);
        return Results.Ok(new { eventId = evt.Id, result = result.ToString() });
    }
}
