using StripePayments.Domain.Catalog;
using StripePayments.Domain.Common;

namespace StripePayments.Domain.Orders;

public enum OrderStatus
{
    Pending,
    Paid,
    PartiallyRefunded,
    Refunded
}

/// <summary>
/// A one-time purchase. Created as <see cref="OrderStatus.Pending"/> when a Checkout Session is opened;
/// every later state change comes from a verified Stripe webhook.
/// </summary>
public sealed class Order
{
    private Order() { }

    public Guid Id { get; private set; }
    public string ProductSku { get; private set; } = default!;
    public string ProductName { get; private set; } = default!;
    public string? CustomerEmail { get; private set; }

    /// <summary>Amount in minor units (cents). List price until paid, then the amount actually charged.</summary>
    public long Amount { get; private set; }
    public long AmountRefunded { get; private set; }
    public string Currency { get; private set; } = default!;
    public OrderStatus Status { get; private set; }

    public string? CheckoutSessionId { get; private set; }
    public string? PaymentIntentId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }

    public long RefundableAmount => Status is OrderStatus.Paid or OrderStatus.PartiallyRefunded ? Amount - AmountRefunded : 0;

    public static Order CreatePending(Product product, string? customerEmail, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ProductSku = product.Sku,
        ProductName = product.Name,
        CustomerEmail = customerEmail,
        Amount = product.UnitAmount,
        Currency = product.Currency,
        Status = OrderStatus.Pending,
        CreatedAt = now,
        UpdatedAt = now
    };

    public void AttachCheckoutSession(string sessionId)
    {
        CheckoutSessionId = sessionId;
    }

    /// <returns><c>false</c> when the order was already paid (replayed or out-of-order event).</returns>
    public bool MarkPaid(string? paymentIntentId, long amountTotal, string? customerEmail, DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending)
            return false;

        Status = OrderStatus.Paid;
        PaymentIntentId = paymentIntentId;
        Amount = amountTotal;
        CustomerEmail ??= customerEmail;
        PaidAt = now;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Applies the cumulative refunded amount reported by Stripe (charge.amount_refunded).</summary>
    public void ApplyRefundedTotal(long totalRefunded, DateTimeOffset now)
    {
        if (Status == OrderStatus.Pending)
            throw new DomainException($"Order {Id} is not paid and cannot be refunded.");
        if (totalRefunded < 0 || totalRefunded > Amount)
            throw new DomainException($"Refunded total {totalRefunded} is outside 0..{Amount}.");
        if (totalRefunded <= AmountRefunded)
            return;

        AmountRefunded = totalRefunded;
        Status = AmountRefunded == Amount ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        UpdatedAt = now;
    }
}
