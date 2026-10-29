using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StripePayments.Domain.Webhooks;

namespace StripePayments.Infrastructure.Persistence.Configurations;

internal sealed class ProcessedStripeEventConfiguration : IEntityTypeConfiguration<ProcessedStripeEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedStripeEvent> b)
    {
        b.ToTable("processed_stripe_events");
        b.HasKey(x => x.EventId);
        b.Property(x => x.EventId).HasMaxLength(255);
        b.Property(x => x.Type).HasMaxLength(100);
    }
}
