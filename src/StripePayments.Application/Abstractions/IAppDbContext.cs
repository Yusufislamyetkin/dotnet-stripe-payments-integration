using Microsoft.EntityFrameworkCore;
using StripePayments.Domain.Catalog;
using StripePayments.Domain.Connect;
using StripePayments.Domain.Customers;
using StripePayments.Domain.Orders;
using StripePayments.Domain.Subscriptions;
using StripePayments.Domain.Webhooks;

namespace StripePayments.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Product> Products { get; }
    DbSet<Plan> Plans { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Order> Orders { get; }
    DbSet<Refund> Refunds { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<ConnectedAccount> ConnectedAccounts { get; }
    DbSet<ProcessedStripeEvent> ProcessedStripeEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
