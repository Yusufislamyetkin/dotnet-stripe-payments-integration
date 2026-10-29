using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StripePayments.Domain.Connect;

namespace StripePayments.Infrastructure.Persistence.Configurations;

internal sealed class ConnectedAccountConfiguration : IEntityTypeConfiguration<ConnectedAccount>
{
    public void Configure(EntityTypeBuilder<ConnectedAccount> b)
    {
        b.ToTable("connected_accounts");
        b.HasKey(x => x.Id);
        b.Property(x => x.StripeAccountId).HasMaxLength(255);
        b.HasIndex(x => x.StripeAccountId).IsUnique();
        b.Property(x => x.Email).HasMaxLength(320);
    }
}
