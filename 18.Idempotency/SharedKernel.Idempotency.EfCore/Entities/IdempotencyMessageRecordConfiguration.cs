using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SharedKernel.Idempotency.EfCore.Entities;

/// <summary>EF Core entity type configuration for <see cref="IdempotencyMessageRecord"/> (D-06).</summary>
internal sealed class IdempotencyMessageRecordConfiguration : IEntityTypeConfiguration<IdempotencyMessageRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdempotencyMessageRecord> builder)
    {
        builder.ToTable("idempotency_messages");

        // The composite primary key IS the unique constraint D-06 calls for — no separate
        // surrogate id column is needed.
        builder.HasKey(x => new { x.TenantId, x.MessageId });

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.MessageId).HasColumnName("message_id").IsRequired();
        builder.Property(x => x.ReservedAtUtc).HasColumnName("reserved_at_utc").IsRequired();
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        builder.Property(x => x.ReservationToken).HasColumnName("reservation_token").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");

        builder.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("ix_idempotency_messages_expires_at_utc");
    }
}
