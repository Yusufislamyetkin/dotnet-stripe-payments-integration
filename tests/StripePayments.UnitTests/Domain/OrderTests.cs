using StripePayments.Domain.Catalog;
using StripePayments.Domain.Common;
using StripePayments.Domain.Orders;

namespace StripePayments.UnitTests.Domain;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Order PaidOrder(long amount = 5000)
    {
        var order = Order.CreatePending(Product.Create("sku", "Name", "", amount, "USD"), null, null, Now);
        order.MarkPaid("pi_1", amount, "a@b.com", Now);
        return order;
    }

    [Fact]
    public void MarkPaid_is_applied_once()
    {
        var order = Order.CreatePending(Product.Create("sku", "Name", "", 5000, "usd"), null, null, Now);

        Assert.True(order.MarkPaid("pi_1", 4500, "a@b.com", Now));
        Assert.False(order.MarkPaid("pi_2", 9999, "x@y.com", Now));

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(4500, order.Amount);
        Assert.Equal("pi_1", order.PaymentIntentId);
    }

    [Fact]
    public void Partial_then_full_refund_updates_status()
    {
        var order = PaidOrder();

        order.ApplyRefundedTotal(2000, Now);
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(3000, order.RefundableAmount);

        order.ApplyRefundedTotal(5000, Now);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0, order.RefundableAmount);
    }

    [Fact]
    public void Stale_refund_total_is_ignored()
    {
        var order = PaidOrder();
        order.ApplyRefundedTotal(3000, Now);

        order.ApplyRefundedTotal(1000, Now);

        Assert.Equal(3000, order.AmountRefunded);
    }

    [Fact]
    public void Refunding_a_pending_order_throws()
    {
        var order = Order.CreatePending(Product.Create("sku", "Name", "", 5000, "usd"), null, null, Now);

        Assert.Throws<DomainException>(() => order.ApplyRefundedTotal(100, Now));
    }

    [Fact]
    public void Changing_product_price_resets_stripe_price()
    {
        var product = Product.Create("sku", "Name", "", 5000, "usd");
        product.AssignStripePrice("price_1");

        product.Update("Name", "", 6000, "usd");

        Assert.Null(product.StripePriceId);
        Assert.Equal("sku_6000_usd", product.StripeLookupKey);
    }
}
