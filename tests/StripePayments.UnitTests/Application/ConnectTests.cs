using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StripePayments.Application.Checkout;
using StripePayments.Application.Common;
using StripePayments.Application.Connect;
using StripePayments.Application.Coupons;
using StripePayments.Application.Webhooks;
using StripePayments.Domain.Catalog;
using StripePayments.Domain.Connect;
using StripePayments.Infrastructure.Persistence;
using StripePayments.Tests.Shared;
using StripePayments.UnitTests.TestSupport;

namespace StripePayments.UnitTests.Application;

public class ConnectTests
{
    private readonly FakeStripeGateway _stripe = new();

    private CheckoutService Checkout(AppDbContext db) => new(db, _stripe, new CouponService(db, _stripe, TimeProvider.System),
        Options.Create(new AppOptions()), Options.Create(new ConnectOptions { PlatformFeePercent = 10 }), TimeProvider.System);

    private static async Task<(AppDbContext, ConnectedAccount)> Seed(bool chargesEnabled)
    {
        var db = TestDb.Create();
        var product = Product.Create("sku-1", "Item", "", 4999, "usd");
        product.AssignStripePrice("price_1");
        var seller = ConnectedAccount.Create("acct_seller", "seller@example.com", DateTimeOffset.UtcNow);
        seller.UpdateCapabilities(chargesEnabled, chargesEnabled, chargesEnabled, DateTimeOffset.UtcNow);
        db.AddRange(product, seller);
        await db.SaveChangesAsync();
        return (db, seller);
    }

    [Fact]
    public async Task Destination_charge_routes_funds_with_platform_fee()
    {
        var (db, seller) = await Seed(chargesEnabled: true);

        await Checkout(db).CreateOneTimeAsync(new OneTimeCheckoutRequest("sku-1", null, ConnectedAccountId: seller.Id), default);

        var spec = Assert.Single(_stripe.Sessions);
        Assert.Equal("acct_seller", spec.DestinationAccountId);
        Assert.Equal(500, spec.ApplicationFeeAmount);
        Assert.Equal(500, (await db.Orders.SingleAsync()).ApplicationFeeAmount);
    }

    [Fact]
    public async Task Platform_fee_is_taken_from_the_discounted_amount()
    {
        var (db, seller) = await Seed(chargesEnabled: true);
        _stripe.PromotionCodes["HALF"] = new PromotionCodeInfo("promo_h", "HALF", true, true, 50, null, null, null, null, 0, null, null);

        await Checkout(db).CreateOneTimeAsync(new OneTimeCheckoutRequest("sku-1", null, "HALF", seller.Id), default);

        Assert.Equal(250, Assert.Single(_stripe.Sessions).ApplicationFeeAmount);
    }

    [Fact]
    public async Task Seller_without_completed_onboarding_cannot_receive_charges()
    {
        var (db, seller) = await Seed(chargesEnabled: false);

        await Assert.ThrowsAsync<ConflictException>(() =>
            Checkout(db).CreateOneTimeAsync(new OneTimeCheckoutRequest("sku-1", null, ConnectedAccountId: seller.Id), default));
        Assert.Empty(_stripe.Sessions);
    }

    [Fact]
    public async Task Account_updated_webhook_syncs_capabilities()
    {
        var (db, seller) = await Seed(chargesEnabled: false);
        var processor = new WebhookProcessor(db, TimeProvider.System, NullLogger<WebhookProcessor>.Instance);

        await processor.ProcessAsync(new StripeEventEnvelope("evt_1", StripeEventTypes.AccountUpdated, DateTimeOffset.UtcNow,
            new AccountUpdated("acct_seller", true, true, true)), default);

        Assert.True(seller.ChargesEnabled);
        Assert.True(seller.PayoutsEnabled);
    }

    [Fact]
    public async Task Create_account_returns_onboarding_link_back_to_the_app()
    {
        await using var db = TestDb.Create();
        var service = new ConnectService(db, _stripe, Options.Create(new AppOptions { PublicBaseUrl = "https://shop.test" }), TimeProvider.System);

        var result = await service.CreateAccountAsync(new CreateConnectedAccountRequest("seller@example.com"), default);

        Assert.Equal("acct_test_1", result.Account.StripeAccountId);
        Assert.Contains(Uri.EscapeDataString("https://shop.test/connect.html"), result.OnboardingUrl);
        Assert.Equal(1, await db.ConnectedAccounts.CountAsync());
    }
}
