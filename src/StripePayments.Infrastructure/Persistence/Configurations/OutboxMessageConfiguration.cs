using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StripePayments.Domain.Outbox;

namespace StripePayments.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasMaxLength(100);
        b.Property(x => x.Payload).HasColumnType("jsonb");
        b.Property(x => x.LastError).HasMaxLength(2000);
        // Partial index: the dispatcher only ever scans pending messages.
        b.HasIndex(x => x.NextAttemptAt).HasFilter("\"DeliveredAt\" IS NULL AND \"DeadLetteredAt\" IS NULL");
        b.Ignore(x => x.IsPending);
    }
}
