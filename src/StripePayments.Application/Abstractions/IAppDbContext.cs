using Microsoft.EntityFrameworkCore;
using StripePayments.Domain.Catalog;
using StripePayments.Domain.Orders;
using StripePayments.Domain.Webhooks;

namespace StripePayments.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Product> Products { get; }
    DbSet<Order> Orders { get; }
    DbSet<ProcessedStripeEvent> ProcessedStripeEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
