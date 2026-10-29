using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StripePayments.Application.Common;
using StripePayments.Application.Refunds;
using StripePayments.Application.Webhooks;
using StripePayments.Domain.Catalog;
using StripePayments.Domain.Orders;
using StripePayments.Infrastructure.Persistence;
using StripePayments.Tests.Shared;
using StripePayments.UnitTests.TestSupport;

namespace StripePayments.UnitTests.Application;

public class RefundTests
{
    private readonly FakeStripeGateway _stripe = new();

    private RefundService Service(AppDbContext db) => new(db, _stripe, TimeProvider.System);

    private static async Task<(AppDbContext, Order)> PaidOrder(long amount = 5000)
    {
        var db = TestDb.Create();
        var order = Order.CreatePending(Product.Create("sku", "Item", "", amount, "usd"), null, null, DateTimeOffset.UtcNow);
        order.MarkPaid("pi_1", amount, null, DateTimeOffset.UtcNow);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (db, order);
    }

    [Fact]
    public async Task Full_refund_uses_remaining_amount_and_idempotency_key()
    {
        var (db, order) = await PaidOrder();

        var refund = await Service(db).RefundAsync(order.Id, new RefundRequest(), default);

        var call = Assert.Single(_stripe.Refunds);
        Assert.Equal(("pi_1", 5000L), (call.PaymentIntentId, call.Amount));
        Assert.Equal(refund.Id.ToString(), call.IdempotencyKey);
        Assert.Equal(OrderStatus.Paid, (await db.Orders.SingleAsync()).Status); // changes only via webhook
    }

    [Fact]
    public async Task Partial_refunds_cannot_exceed_paid_amount_before_webhook_arrives()
    {
        var (db, order) = await PaidOrder();
        await Service(db).RefundAsync(order.Id, new RefundRequest(3000), default);

        await Assert.ThrowsAsync<ConflictException>(() => Service(db).RefundAsync(order.Id, new RefundRequest(2500), default));
        await Service(db).RefundAsync(order.Id, new RefundRequest(2000), default);

        Assert.Equal(2, _stripe.Refunds.Count);
    }

    [Fact]
    public async Task Pending_order_cannot_be_refunded()
    {
        await using var db = TestDb.Create();
        var order = Order.CreatePending(Product.Create("sku", "Item", "", 5000, "usd"), null, null, DateTimeOffset.UtcNow);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => Service(db).RefundAsync(order.Id, new RefundRequest(), default));
    }

    [Fact]
    public async Task Charge_refunded_webhook_updates_order_status()
    {
        var (db, order) = await PaidOrder();
        var processor = new WebhookProcessor(db, TimeProvider.System, NullLogger<WebhookProcessor>.Instance);

        await processor.ProcessAsync(new StripeEventEnvelope("evt_1", StripeEventTypes.ChargeRefunded, DateTimeOffset.UtcNow,
            new ChargeRefunded("ch_1", "pi_1", 5000, 2000)), default);
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);

        await processor.ProcessAsync(new StripeEventEnvelope("evt_2", StripeEventTypes.ChargeRefunded, DateTimeOffset.UtcNow,
            new ChargeRefunded("ch_1", "pi_1", 5000, 5000)), default);
        Assert.Equal(OrderStatus.Refunded, order.Status);
    }

    [Theory]
    [InlineData(0L, null, false)]
    [InlineData(100L, "because", false)]
    [InlineData(100L, "requested_by_customer", true)]
    [InlineData(null, null, true)]
    public void Validator_rules(long? amount, string? reason, bool valid) =>
        Assert.Equal(valid, new RefundRequestValidator().Validate(new RefundRequest(amount, reason)).IsValid);
}
