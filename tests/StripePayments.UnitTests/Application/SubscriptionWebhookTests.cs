using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StripePayments.Application.Webhooks;
using StripePayments.Domain.Catalog;
using StripePayments.Domain.Subscriptions;
using StripePayments.Infrastructure.Persistence;
using StripePayments.UnitTests.TestSupport;

namespace StripePayments.UnitTests.Application;

public class SubscriptionWebhookTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static WebhookProcessor Processor(AppDbContext db) =>
        new(db, TimeProvider.System, NullLogger<WebhookProcessor>.Instance);

    private static StripeEventEnvelope SubEvent(string id, string type, DateTimeOffset created, string status,
        string priceId = "price_pro", bool cancelAtPeriodEnd = false) =>
        new(id, type, created, new SubscriptionChanged("sub_1", "cus_1", status, priceId,
            T0.AddDays(30), T0.AddDays(14), cancelAtPeriodEnd, null));

    private static async Task<AppDbContext> DbWithPlans()
    {
        var db = TestDb.Create();
        var pro = Plan.Create("pro-monthly", "Pro", BillingInterval.Month, 2900, "usd", 14);
        pro.AssignStripePrice("price_pro");
        var starter = Plan.Create("starter-monthly", "Starter", BillingInterval.Month, 900, "usd", 0);
        starter.AssignStripePrice("price_starter");
        db.Plans.AddRange(pro, starter);
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Created_event_creates_trialing_subscription_with_plan()
    {
        await using var db = await DbWithPlans();

        await Processor(db).ProcessAsync(SubEvent("evt_1", StripeEventTypes.SubscriptionCreated, T0, "trialing"), default);

        var sub = await db.Subscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Trialing, sub.Status);
        Assert.Equal("pro-monthly", sub.PlanCode);
        Assert.Equal(T0.AddDays(14), sub.TrialEnd);
    }

    [Fact]
    public async Task Checkout_completed_before_subscription_event_is_merged_into_one_row()
    {
        await using var db = await DbWithPlans();
        var checkout = new StripeEventEnvelope("evt_c", StripeEventTypes.CheckoutSessionCompleted, T0,
            new CheckoutSessionCompleted("cs_1", "subscription", "paid", null, "sub_1", "cus_1", "a@b.com", 0,
                new Dictionary<string, string> { ["plan_code"] = "pro-monthly" }));

        await Processor(db).ProcessAsync(checkout, default);
        await Processor(db).ProcessAsync(SubEvent("evt_1", StripeEventTypes.SubscriptionCreated, T0, "active"), default);

        var sub = await db.Subscriptions.SingleAsync();
        Assert.Equal("a@b.com", sub.CustomerEmail);
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.Equal(1, await db.Customers.CountAsync());
    }

    [Fact]
    public async Task Plan_change_is_reflected_from_updated_event()
    {
        await using var db = await DbWithPlans();
        await Processor(db).ProcessAsync(SubEvent("evt_1", StripeEventTypes.SubscriptionCreated, T0, "active"), default);

        await Processor(db).ProcessAsync(SubEvent("evt_2", StripeEventTypes.SubscriptionUpdated, T0.AddMinutes(1), "active", "price_starter"), default);

        Assert.Equal("starter-monthly", (await db.Subscriptions.SingleAsync()).PlanCode);
    }

    [Fact]
    public async Task Older_snapshot_arriving_late_is_ignored()
    {
        await using var db = await DbWithPlans();
        await Processor(db).ProcessAsync(SubEvent("evt_new", StripeEventTypes.SubscriptionDeleted, T0.AddMinutes(5), "canceled"), default);

        var result = await Processor(db).ProcessAsync(SubEvent("evt_old", StripeEventTypes.SubscriptionUpdated, T0, "active"), default);

        Assert.Equal(WebhookResult.Ignored, result);
        Assert.Equal(SubscriptionStatus.Canceled, (await db.Subscriptions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Invoice_events_record_payment_and_failure()
    {
        await using var db = await DbWithPlans();
        await Processor(db).ProcessAsync(SubEvent("evt_1", StripeEventTypes.SubscriptionCreated, T0, "active"), default);

        await Processor(db).ProcessAsync(new StripeEventEnvelope("evt_2", StripeEventTypes.InvoicePaid, T0,
            new InvoiceEvent("in_1", "sub_1", "cus_1", 2900, 2900)), default);
        await Processor(db).ProcessAsync(new StripeEventEnvelope("evt_3", StripeEventTypes.InvoicePaymentFailed, T0,
            new InvoiceEvent("in_2", "sub_1", "cus_1", 0, 2900)), default);

        var sub = await db.Subscriptions.SingleAsync();
        Assert.NotNull(sub.LastPaymentAt);
        Assert.NotNull(sub.LastPaymentFailedAt);
    }
}
