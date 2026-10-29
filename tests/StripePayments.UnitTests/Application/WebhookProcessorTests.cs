using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StripePayments.Application.Webhooks;
using StripePayments.Domain.Catalog;
using StripePayments.Domain.Orders;
using StripePayments.Infrastructure.Persistence;
using StripePayments.UnitTests.TestSupport;

namespace StripePayments.UnitTests.Application;

public class WebhookProcessorTests
{
    private static WebhookProcessor Processor(AppDbContext db) =>
        new(db, TimeProvider.System, NullLogger<WebhookProcessor>.Instance);

    private static async Task<Order> SeedPendingOrder(AppDbContext db, string sessionId = "cs_1")
    {
        var order = Order.CreatePending(Product.Create("sku", "Item", "", 5000, "usd"), null, DateTimeOffset.UtcNow);
        order.AttachCheckoutSession(sessionId);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static StripeEventEnvelope Completed(string eventId, string sessionId = "cs_1", string paymentStatus = "paid", long amount = 4500) =>
        new(eventId, StripeEventTypes.CheckoutSessionCompleted, DateTimeOffset.UtcNow, new CheckoutSessionCompleted(
            sessionId, "payment", paymentStatus, "pi_1", null, "cus_1", "buyer@example.com", amount,
            new Dictionary<string, string>()));

    [Fact]
    public async Task Checkout_completed_marks_order_paid()
    {
        await using var db = TestDb.Create();
        var order = await SeedPendingOrder(db);

        var result = await Processor(db).ProcessAsync(Completed("evt_1"), default);

        Assert.Equal(WebhookResult.Processed, result);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(4500, order.Amount);
        Assert.Equal("buyer@example.com", order.CustomerEmail);
    }

    [Fact]
    public async Task Same_event_delivered_twice_is_applied_once()
    {
        var name = Guid.NewGuid().ToString();
        await using (var db = TestDb.Create(name))
        {
            await SeedPendingOrder(db);
            Assert.Equal(WebhookResult.Processed, await Processor(db).ProcessAsync(Completed("evt_1"), default));
        }

        await using (var db = TestDb.Create(name))
        {
            Assert.Equal(WebhookResult.Duplicate, await Processor(db).ProcessAsync(Completed("evt_1", amount: 1), default));
            Assert.Equal(4500, (await db.Orders.SingleAsync()).Amount);
            Assert.Equal(1, await db.ProcessedStripeEvents.CountAsync());
        }
    }

    [Fact]
    public async Task Different_event_for_already_paid_order_does_not_change_it()
    {
        await using var db = TestDb.Create();
        var order = await SeedPendingOrder(db);
        await Processor(db).ProcessAsync(Completed("evt_1"), default);

        var result = await Processor(db).ProcessAsync(Completed("evt_2", amount: 1), default);

        Assert.Equal(WebhookResult.Ignored, result);
        Assert.Equal(4500, order.Amount);
    }

    [Fact]
    public async Task Unpaid_async_payment_keeps_order_pending()
    {
        await using var db = TestDb.Create();
        var order = await SeedPendingOrder(db);

        await Processor(db).ProcessAsync(Completed("evt_1", paymentStatus: "unpaid"), default);

        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task Unknown_event_is_recorded_and_ignored()
    {
        await using var db = TestDb.Create();

        var result = await Processor(db).ProcessAsync(new StripeEventEnvelope("evt_x", "customer.created", DateTimeOffset.UtcNow, null), default);

        Assert.Equal(WebhookResult.Ignored, result);
        var marker = await db.ProcessedStripeEvents.SingleAsync();
        Assert.False(marker.Handled);
    }
}
