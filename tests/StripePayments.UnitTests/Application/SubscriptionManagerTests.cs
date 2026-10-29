using Microsoft.Extensions.Options;
using StripePayments.Application.Abstractions;
using StripePayments.Application.Common;
using StripePayments.Application.Coupons;
using StripePayments.Application.Subscriptions;
using StripePayments.Domain.Catalog;
using StripePayments.Domain.Subscriptions;
using StripePayments.Infrastructure.Persistence;
using StripePayments.Tests.Shared;
using StripePayments.UnitTests.TestSupport;

namespace StripePayments.UnitTests.Application;

public class SubscriptionManagerTests
{
    private readonly FakeStripeGateway _stripe = new();

    private SubscriptionManager Manager(AppDbContext db) =>
        new(db, _stripe, new CouponService(db, _stripe, TimeProvider.System), Options.Create(new AppOptions { PublicBaseUrl = "https://shop.test" }));

    private static async Task<(AppDbContext Db, Subscription Sub)> Seed(SubscriptionStatus status = SubscriptionStatus.Active)
    {
        var db = TestDb.Create();
        var pro = Plan.Create("pro-monthly", "Pro", BillingInterval.Month, 2900, "usd", 14);
        pro.AssignStripePrice("price_pro");
        var yearly = Plan.Create("pro-yearly", "Pro yearly", BillingInterval.Year, 29000, "usd", 14);
        yearly.AssignStripePrice("price_yearly");
        db.Plans.AddRange(pro, yearly);

        var sub = Subscription.Create("sub_1", "cus_1", DateTimeOffset.UtcNow);
        sub.Sync(new SubscriptionSnapshot(status, "price_pro", "pro-monthly", null, null, false, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();
        return (db, sub);
    }

    [Fact]
    public async Task Checkout_uses_plan_price_and_trial()
    {
        var (db, _) = await Seed();

        await Manager(db).CreateCheckoutAsync(new SubscriptionCheckoutRequest("pro-monthly", "a@b.com"), default);

        var spec = Assert.Single(_stripe.Sessions);
        Assert.Equal(CheckoutMode.Subscription, spec.Mode);
        Assert.Equal("price_pro", spec.PriceId);
        Assert.Equal(14, spec.TrialDays);
    }

    [Fact]
    public async Task Change_plan_calls_stripe_with_new_price()
    {
        var (db, sub) = await Seed();

        await Manager(db).ChangePlanAsync(sub.Id, new ChangePlanRequest("pro-yearly"), default);

        Assert.Contains("change:sub_1:price_yearly", _stripe.Calls);
    }

    [Fact]
    public async Task Change_to_same_plan_is_conflict()
    {
        var (db, sub) = await Seed();

        await Assert.ThrowsAsync<ConflictException>(() => Manager(db).ChangePlanAsync(sub.Id, new ChangePlanRequest("pro-monthly"), default));
    }

    [Fact]
    public async Task Canceled_subscription_cannot_be_canceled_again()
    {
        var (db, sub) = await Seed(SubscriptionStatus.Canceled);

        await Assert.ThrowsAsync<ConflictException>(() => Manager(db).CancelAsync(sub.Id, new CancelSubscriptionRequest(), default));
        Assert.Empty(_stripe.Calls);
    }

    [Fact]
    public async Task Portal_link_uses_stripe_customer()
    {
        var (db, sub) = await Seed();

        var link = await Manager(db).CreatePortalLinkAsync(sub.Id, default);

        Assert.Equal("https://billing.stripe.test/cus_1", link.Url);
    }
}
