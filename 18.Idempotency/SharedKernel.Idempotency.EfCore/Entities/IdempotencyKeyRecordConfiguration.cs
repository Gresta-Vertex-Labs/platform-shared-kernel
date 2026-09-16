using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SharedKernel.Idempotency.EfCore.Entities;

/// <summary>EF Core entity type configuration for <see cref="IdempotencyKeyRecord"/>.</summary>
internal sealed class IdempotencyKeyRecordConfiguration : IEntityTypeConfiguration<IdempotencyKeyRecord>
{
    /// <summary>Sourced from the caller — no meaningful upper bound other than "generous enough for a real key".</summary>
    private const int KeyMaxLength = 512;

    /// <summary>A SHA-256 hex fingerprint is 64 characters; generous headroom for a different hash algorithm.</summary>
    private const int FingerprintMaxLength = 128;

    /// <summary>Longest current <see cref="IdempotencyRecordStatus"/> member name is "InProgress" (10 chars).</summary>
    private const int StatusMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdempotencyKeyRecord> builder)
    {
        builder.ToTable("idempotency_keys");

        // The composite primary key IS the unique constraint this table needs — no separate
        // surrogate id column is needed.
        builder.HasKey(x => new { x.TenantId, x.Key });

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(KeyMaxLength).IsRequired();
        builder.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(FingerprintMaxLength).IsRequired();

        // Stored as text via the enum's member name (HasConversion<string>() default ToString()
        // serialization) — the raw-SQL upsert in EfCoreRequestIdempotencyStore writes the identical
        // literal strings via Internal.IdempotencyRecordStatusNames, which must stay in sync with
        // this enum's member names.
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(StatusMaxLength).IsRequired();

        builder.Property(x => x.ReservationToken).HasColumnName("reservation_token").IsRequired();
        builder.Property(x => x.ReservedAtUtc).HasColumnName("reserved_at_utc").IsRequired();
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();

        // Opaque payload, plain text, never jsonb — this package must never parse or validate
        // caller-supplied response content.
        builder.Property(x => x.Response).HasColumnName("response").HasColumnType("text");

        // Non-unique index supporting the documented cleanup-recipe scan and the expired-row
        // exclusion filter on every read.
        builder.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("ix_idempotency_keys_expires_at_utc");
    }
}
