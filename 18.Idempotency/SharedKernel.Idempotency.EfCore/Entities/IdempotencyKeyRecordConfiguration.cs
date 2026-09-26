using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Idempotency.Abstractions;

namespace SharedKernel.Idempotency.EfCore.Entities;

/// <summary>EF Core entity type configuration for <see cref="IdempotencyKeyRecord"/>.</summary>
internal sealed class IdempotencyKeyRecordConfiguration : IEntityTypeConfiguration<IdempotencyKeyRecord>
{
    /// <summary>The table name. Shared with the raw-SQL upsert in <see cref="Store.EfCoreIdempotencyStore"/>.</summary>
    internal const string TableName = "idempotency_keys";

    /// <summary>Sourced from the caller — no meaningful upper bound other than "generous enough for a real key".</summary>
    private const int KeyMaxLength = 512;

    /// <summary>A SHA-256 hex fingerprint is 64 characters; generous headroom for a different hash algorithm.</summary>
    private const int FingerprintMaxLength = 128;

    /// <summary>Longest <see cref="IdempotencyRecordStatus"/> member name is "InProgress" (10 characters).</summary>
    private const int StatusMaxLength = 20;

    /// <summary>Longest <see cref="IdempotencyPurpose"/> member name is "Request" (7 characters).</summary>
    private const int PurposeMaxLength = 16;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdempotencyKeyRecord> builder)
    {
        builder.ToTable(TableName);

        // The composite primary key IS the unique constraint the atomic upsert conflicts on.
        builder.HasKey(x => new { x.TenantScope, x.Purpose, x.Key });

        builder.Property(x => x.TenantScope).HasColumnName("tenant_scope").HasMaxLength(IdempotencyTenantScope.MaxLength).IsRequired();

        // Enum member names as text; the raw-SQL upsert binds the same strings (IdempotencyRecordStatusNames,
        // purpose.ToString()), so the names must stay in sync with the enums.
        builder.Property(x => x.Purpose).HasColumnName("purpose").HasConversion<string>().HasMaxLength(PurposeMaxLength).IsRequired();
        builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(KeyMaxLength).IsRequired();
        builder.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(FingerprintMaxLength).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(StatusMaxLength).IsRequired();
        builder.Property(x => x.ReservationToken).HasColumnName("reservation_token").IsRequired();
        builder.Property(x => x.ReservedAtUtc).HasColumnName("reserved_at_utc").IsRequired();
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();

        // Opaque payload, plain text, never jsonb — this package never parses caller-supplied responses.
        builder.Property(x => x.Response).HasColumnName("response").HasColumnType("text");

        // Supports the documented cleanup job's scan.
        builder.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("ix_idempotency_keys_expires_at_utc");
    }
}
