using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StripePayments.Domain.Catalog;
using StripePayments.Domain.Customers;
using StripePayments.Domain.Subscriptions;

namespace StripePayments.Infrastructure.Persistence.Configurations;

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> b)
    {
        b.ToTable("plans");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Code).HasMaxLength(64);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Interval).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.StripePriceId).HasMaxLength(255);
        b.HasIndex(x => x.StripePriceId);
        b.Ignore(x => x.StripeInterval);
        b.Ignore(x => x.StripeLookupKey);
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers");
        b.HasKey(x => x.Id);
        b.Property(x => x.StripeCustomerId).HasMaxLength(255);
        b.HasIndex(x => x.StripeCustomerId).IsUnique();
        b.Property(x => x.Email).HasMaxLength(320);
    }
}

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> b)
    {
        b.ToTable("subscriptions");
        b.HasKey(x => x.Id);
        b.Property(x => x.StripeSubscriptionId).HasMaxLength(255);
        b.HasIndex(x => x.StripeSubscriptionId).IsUnique();
        b.Property(x => x.StripeCustomerId).HasMaxLength(255);
        b.Property(x => x.CustomerEmail).HasMaxLength(320);
        b.Property(x => x.PlanCode).HasMaxLength(64);
        b.Property(x => x.StripePriceId).HasMaxLength(255);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        b.Ignore(x => x.IsLive);
    }
}
