using Microsoft.EntityFrameworkCore;
using StripePayments.Domain.Catalog;
using StripePayments.Domain.Orders;

namespace StripePayments.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Product> Products { get; }
    DbSet<Order> Orders { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
