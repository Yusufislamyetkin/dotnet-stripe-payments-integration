using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StripePayments.Application.Abstractions;
using StripePayments.Application.Common;
using StripePayments.Domain.Orders;

namespace StripePayments.Application.Refunds;

/// <param name="Amount">Minor units. Omit for a full refund of the remaining amount.</param>
public sealed record RefundRequest(long? Amount = null, string? Reason = null);

public sealed record RefundDto(Guid Id, Guid OrderId, long Amount, string? Reason, string? StripeRefundId, string? StripeStatus, DateTimeOffset CreatedAt)
{
    public static RefundDto From(Refund r) => new(r.Id, r.OrderId, r.Amount, r.Reason, r.StripeRefundId, r.StripeStatus, r.CreatedAt);
}

public sealed class RefundRequestValidator : AbstractValidator<RefundRequest>
{
    public static readonly string[] StripeReasons = ["duplicate", "fraudulent", "requested_by_customer"];

    public RefundRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).When(x => x.Amount is not null);
        RuleFor(x => x.Reason).Must(r => StripeReasons.Contains(r))
            .When(x => x.Reason is not null)
            .WithMessage($"Reason must be one of: {string.Join(", ", StripeReasons)}.");
    }
}

public sealed class RefundService(IAppDbContext db, IStripeGateway stripe, TimeProvider clock)
{
    public async Task<RefundDto> RefundAsync(Guid orderId, RefundRequest request, CancellationToken ct)
    {
        if (!stripe.IsConfigured) throw new StripeNotConfiguredException();

        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new NotFoundException("Order", orderId);
        if (order.PaymentIntentId is null || order.RefundableAmount == 0)
            throw new ConflictException($"Order is {order.Status} and has nothing left to refund.");

        // Refunds requested here may not be reflected in AmountRefunded until charge.refunded arrives.
        var requested = await db.Refunds.Where(r => r.OrderId == orderId && r.StripeRefundId != null).SumAsync(r => r.Amount, ct);
        var available = order.Amount - Math.Max(order.AmountRefunded, requested);

        var amount = request.Amount ?? available;
        if (amount <= 0 || amount > available)
            throw new ConflictException($"Refund amount must be between 1 and {available} (minor units).");

        var refund = Refund.Request(order.Id, amount, request.Reason, clock.GetUtcNow());
        // The local refund id doubles as Stripe's idempotency key, so a retried request cannot refund twice.
        var result = await stripe.CreateRefundAsync(order.PaymentIntentId, amount, request.Reason, refund.Id.ToString(), ct);
        refund.Accepted(result.RefundId, result.Status);

        db.Refunds.Add(refund);
        await db.SaveChangesAsync(ct);
        return RefundDto.From(refund);
    }

    public async Task<IReadOnlyList<RefundDto>> ListAsync(Guid orderId, CancellationToken ct) =>
        (await db.Refunds.AsNoTracking().Where(r => r.OrderId == orderId).OrderBy(r => r.CreatedAt).ToListAsync(ct))
        .Select(RefundDto.From).ToList();
}
