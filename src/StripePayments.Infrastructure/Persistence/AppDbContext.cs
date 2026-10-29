using Microsoft.EntityFrameworkCore;
using StripePayments.Application.Abstractions;
using StripePayments.Domain.Catalog;
using StripePayments.Domain.Customers;
using StripePayments.Domain.Orders;
using StripePayments.Domain.Subscriptions;
using StripePayments.Domain.Webhooks;

namespace StripePayments.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<ProcessedStripeEvent> ProcessedStripeEvents => Set<ProcessedStripeEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
