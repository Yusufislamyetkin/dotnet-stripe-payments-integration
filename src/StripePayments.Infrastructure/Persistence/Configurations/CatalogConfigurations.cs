using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StripePayments.Domain.Catalog;

namespace StripePayments.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.Sku).IsUnique();
        b.Property(x => x.Sku).HasMaxLength(64);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.StripePriceId).HasMaxLength(255);
        b.Ignore(x => x.StripeLookupKey);
    }
}
