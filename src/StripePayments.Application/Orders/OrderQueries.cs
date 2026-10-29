using Microsoft.EntityFrameworkCore;
using StripePayments.Application.Abstractions;
using StripePayments.Application.Common;
using StripePayments.Domain.Orders;

namespace StripePayments.Application.Orders;

public sealed record OrderDto(
    Guid Id,
    string ProductSku,
    string ProductName,
    string? CustomerEmail,
    string? PromotionCode,
    long Amount,
    long AmountRefunded,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt)
{
    public static OrderDto From(Order o) => new(o.Id, o.ProductSku, o.ProductName, o.CustomerEmail, o.PromotionCode, o.Amount,
        o.AmountRefunded, o.Currency, o.Status.ToString(), o.CreatedAt, o.PaidAt);
}

public sealed class OrderQueries(IAppDbContext db)
{
    public async Task<IReadOnlyList<OrderDto>> ListAsync(int take, CancellationToken ct) =>
        (await db.Orders.AsNoTracking().OrderByDescending(o => o.CreatedAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct))
        .Select(OrderDto.From).ToList();

    public async Task<OrderDto> GetAsync(Guid id, CancellationToken ct) =>
        OrderDto.From(await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException("Order", id));
}
