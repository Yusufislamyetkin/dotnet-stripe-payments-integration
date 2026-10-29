namespace StripePayments.Domain.Orders;

/// <summary>
/// A refund requested through this API. The order's refunded total is not changed here;
/// it follows the charge.refunded webhook.
/// </summary>
public sealed class Refund
{
    private Refund() { }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public long Amount { get; private set; }
    public string? Reason { get; private set; }
    public string? StripeRefundId { get; private set; }
    public string? StripeStatus { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Refund Request(Guid orderId, long amount, string? reason, DateTimeOffset now) => amount <= 0
        ? throw new ArgumentOutOfRangeException(nameof(amount))
        : new Refund { Id = Guid.NewGuid(), OrderId = orderId, Amount = amount, Reason = reason, CreatedAt = now };

    public void Accepted(string stripeRefundId, string stripeStatus)
    {
        StripeRefundId = stripeRefundId;
        StripeStatus = stripeStatus;
    }
}
