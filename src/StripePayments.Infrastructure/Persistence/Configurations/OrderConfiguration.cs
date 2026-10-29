using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StripePayments.Domain.Orders;

namespace StripePayments.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders");
        b.HasKey(x => x.Id);
        b.Property(x => x.ProductSku).HasMaxLength(64);
        b.Property(x => x.ProductName).HasMaxLength(200);
        b.Property(x => x.CustomerEmail).HasMaxLength(320);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.CheckoutSessionId).HasMaxLength(255);
        b.Property(x => x.PaymentIntentId).HasMaxLength(255);
        b.HasIndex(x => x.CheckoutSessionId).IsUnique();
        b.HasIndex(x => x.PaymentIntentId);
        b.Ignore(x => x.RefundableAmount);
    }
}
