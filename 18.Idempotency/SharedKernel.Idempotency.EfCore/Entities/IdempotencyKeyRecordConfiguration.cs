using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SharedKernel.Idempotency.EfCore.Entities;

/// <summary>EF Core entity type configuration for <see cref="IdempotencyKeyRecord"/> (D-06).</summary>
internal sealed class IdempotencyKeyRecordConfiguration : IEntityTypeConfiguration<IdempotencyKeyRecord>
{
    /// <summary>Sourced from the caller — no meaningful upper bound other than "generous enough for a real key".</summary>
    private const int KeyMaxLength = 512;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdempotencyKeyRecord> builder)
    {
        builder.ToTable("idempotency_keys");

        // The composite primary key IS the unique constraint D-06 calls for — no separate
        // surrogate id column is needed.
        builder.HasKey(x => new { x.TenantId, x.Key });

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(KeyMaxLength).IsRequired();
        builder.Property(x => x.ReservedAtUtc).HasColumnName("reserved_at_utc").IsRequired();
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();

        // D-121-style deviation, recorded here rather than silently: opaque payload, plain text,
        // never jsonb — this package must never parse or validate caller-supplied response content
        // (Domain Invariant 6).
        builder.Property(x => x.Response).HasColumnName("response").HasColumnType("text");

        // Non-unique index supporting the documented cleanup-recipe scan and the expired-row
        // exclusion filter on every read (D-06).
        builder.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("ix_idempotency_keys_expires_at_utc");
    }
}
