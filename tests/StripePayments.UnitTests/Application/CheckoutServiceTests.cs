using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StripePayments.Application.Abstractions;
using StripePayments.Application.Checkout;
using StripePayments.Application.Common;
using StripePayments.Application.Connect;
using StripePayments.Application.Coupons;
using StripePayments.Domain.Catalog;
using StripePayments.Domain.Orders;
using StripePayments.UnitTests.TestSupport;

namespace StripePayments.UnitTests.Application;

public class CheckoutServiceTests
{
    private readonly FakeStripeGateway _stripe = new();

    private CheckoutService CreateService(StripePayments.Infrastructure.Persistence.AppDbContext db) =>
        new(db, _stripe, new CouponService(db, _stripe, TimeProvider.System), Options.Create(new AppOptions { PublicBaseUrl = "https://shop.test/" }), Options.Create(new ConnectOptions()), TimeProvider.System);

    [Fact]
    public async Task Creates_pending_order_linked_to_checkout_session()
    {
        await using var db = TestDb.Create();
        var product = Product.Create("sku-1", "Item", "", 2500, "usd");
        product.AssignStripePrice("price_123");
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var result = await CreateService(db).CreateOneTimeAsync(new OneTimeCheckoutRequest("sku-1", "a@b.com"), default);

        var order = await db.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(result.SessionId, order.CheckoutSessionId);
        var spec = Assert.Single(_stripe.Sessions);
        Assert.Equal(CheckoutMode.Payment, spec.Mode);
        Assert.Equal("price_123", spec.PriceId);
        Assert.Equal(order.Id.ToString(), spec.Metadata[CheckoutService.OrderIdMetadataKey]);
        Assert.StartsWith("https://shop.test/success.html", spec.SuccessUrl);
    }

    [Fact]
    public async Task Missing_stripe_key_gives_clear_error()
    {
        await using var db = TestDb.Create();
        _stripe.IsConfigured = false;

        await Assert.ThrowsAsync<StripeNotConfiguredException>(() =>
            CreateService(db).CreateOneTimeAsync(new OneTimeCheckoutRequest("sku-1", null), default));
    }

    [Fact]
    public async Task Unknown_sku_is_not_found()
    {
        await using var db = TestDb.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateService(db).CreateOneTimeAsync(new OneTimeCheckoutRequest("nope", null), default));
    }
}
