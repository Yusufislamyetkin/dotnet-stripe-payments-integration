using StripePayments.Application.Catalog;
using StripePayments.Application.Checkout;
using StripePayments.Application.Orders;

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

        var orders = api.MapGroup("/orders").WithTags("Orders");
        orders.MapGet("/", (OrderQueries q, CancellationToken ct, int take = 50) => q.ListAsync(take, ct));
        orders.MapGet("/{id:guid}", (Guid id, OrderQueries q, CancellationToken ct) => q.GetAsync(id, ct));

        return app;
    }
}
