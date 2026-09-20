using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Persistence.Abstractions.Auditing;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// EF Core entity type configuration for <see cref="AuditRecord"/>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately implements <see cref="IEntityTypeConfiguration{TEntity}"/>
/// directly rather than extending <c>EntityTypeConfigurationBase&lt;TEntity,TId&gt;</c> —
/// <see cref="AuditRecord"/> is a plain infrastructure record, not an aggregate root, and carries none
/// of that base's marker interfaces (<c>IHasConcurrency</c>, <c>ISoftDeletable</c>,
/// <c>IHasCreatedAudit</c>/<c>IHasAudit</c>, <c>IHasTenant</c> — <see cref="AuditRecord.TenantId"/> is
/// nullable, which <c>IHasTenant</c> does not allow).
/// </para>
/// <para>
/// Every column name is pinned explicitly — see <see cref="AuditSchema"/>'s remarks for why the raw-ADO
/// writer needs this independent of whatever naming convention the host <c>DbContext</c> applies.
/// </para>
/// <para>
/// Applied only when <c>EfCorePersistenceBuilder{TContext}.WithAuditTrail()</c> has been called — see
/// <see cref="AuditRecordModelConfigurator"/>.
/// </para>
/// </remarks>
public sealed class AuditRecordEntityConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    /// <summary>Hex HMAC-SHA256 digest length (32 bytes → 64 hex characters).</summary>
    private const int HashColumnLength = 64;

    /// <summary>Matches the platform's existing audit-string-format column length.</summary>
    private const int IdentityColumnLength = 256;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable(AuditSchema.TableName);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName(AuditSchema.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantId).HasColumnName(AuditSchema.TenantId);

        builder.Property(x => x.ActorId).HasColumnName(AuditSchema.ActorId).IsRequired().HasMaxLength(IdentityColumnLength);
        builder.Property(x => x.ActorKind).HasColumnName(AuditSchema.ActorKind).HasConversion<int>().IsRequired();

        builder.Property(x => x.Action).HasColumnName(AuditSchema.Action).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ResourceType).HasColumnName(AuditSchema.ResourceType).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ResourceId).HasColumnName(AuditSchema.ResourceId).IsRequired().HasMaxLength(200);

        builder.Property(x => x.Sequence).HasColumnName(AuditSchema.Sequence).IsRequired();
        builder.Property(x => x.OccurredOn).HasColumnName(AuditSchema.OccurredOn).IsRequired();

        // Opaque snapshot columns — plain text, NEVER jsonb. This package must never parse or
        // validate caller-supplied snapshot content.
        builder.Property(x => x.BeforeSnapshot).HasColumnName(AuditSchema.BeforeSnapshot).HasColumnType("text");
        builder.Property(x => x.AfterSnapshot).HasColumnName(AuditSchema.AfterSnapshot).HasColumnType("text");

        builder.Property(x => x.CorrelationId).HasColumnName(AuditSchema.CorrelationId).HasMaxLength(IdentityColumnLength);
        builder.Property(x => x.ApprovalId).HasColumnName(AuditSchema.ApprovalId).HasMaxLength(IdentityColumnLength);

        builder.Property(x => x.Outcome).HasColumnName(AuditSchema.Outcome).HasConversion<int>().IsRequired();
        builder.Property(x => x.ErrorCode).HasColumnName(AuditSchema.ErrorCode).HasMaxLength(500);
        builder.Property(x => x.ClientId).HasColumnName(AuditSchema.ClientId).HasMaxLength(IdentityColumnLength);
        builder.Property(x => x.SessionId).HasColumnName(AuditSchema.SessionId).HasMaxLength(IdentityColumnLength);
        builder.Property(x => x.ImpersonatorId).HasColumnName(AuditSchema.ImpersonatorId).HasMaxLength(IdentityColumnLength);
        builder.Property(x => x.SourceService).HasColumnName(AuditSchema.SourceService).HasMaxLength(200);
        builder.Property(x => x.IdempotencyKey).HasColumnName(AuditSchema.IdempotencyKey).HasMaxLength(IdentityColumnLength);

        builder.Property(x => x.HashAlgorithm).HasColumnName(AuditSchema.HashAlgorithm).IsRequired().HasMaxLength(32);
        builder.Property(x => x.SchemaVersion).HasColumnName(AuditSchema.SchemaVersion).IsRequired();
        builder.Property(x => x.KeyId).HasColumnName(AuditSchema.KeyId).IsRequired().HasMaxLength(100);

        builder.Property(x => x.RecordHash).HasColumnName(AuditSchema.RecordHash).IsRequired().HasMaxLength(HashColumnLength);
        builder.Property(x => x.PreviousRecordHash).HasColumnName(AuditSchema.PreviousRecordHash).HasMaxLength(HashColumnLength);

        // The non-nullable chain-partition key: a database-computed, STORED generated column, never
        // written by the application — see AuditChainKeyFormat's remarks for why TenantId (nullable)
        // cannot itself carry the chain's uniqueness/lookup key, and why this must be computed
        // IDENTICALLY (same COALESCE-and-concatenate shape, same 'system' literal) to
        // AuditChainKeyFormat.Build's C# logic, which the writer uses to query this same column.
        builder.Property<string>(AuditSchema.ChainKey)
            .HasColumnName(AuditSchema.ChainKey)
                .HasComputedColumnSql(
                $"COALESCE(\"{AuditSchema.TenantId}\"::text, 'system') || '|' || \"{AuditSchema.ResourceType}\"",
                stored: true)
                    .HasMaxLength(300)
                        .IsRequired();

        // Chain uniqueness + "read the chain head" lookup (ORDER BY sequence DESC LIMIT 1 WHERE
        // chain_key = @k) — the one index every append and every full-chain verification relies on.
        // NOTE: HasIndex(params string[]) takes EF PROPERTY names, never column names — ChainKey
        // (a shadow property) happens to share its logical name with its column name, but Sequence/
        // ResourceId/IdempotencyKey below are real CLR properties and must use nameof(...), not
        // AuditSchema's column-name constants (a real bug caught by SQLite/Postgres model-building,
        // not by inspection: EF silently tried to create a NEW shadow property for a lowercase
        // "sequence" that doesn't match the CLR property "Sequence").
        builder.HasIndex(AuditSchema.ChainKey, nameof(AuditRecord.Sequence))
            .IsUnique()
                .HasDatabaseName(AuditSchema.IndexChainSequence);

        // Retry-safe RecordAsync: a partial unique index (NULLs excluded) so multiple records with no
        // idempotency key never collide, while two records in the same chain sharing a REAL
        // idempotency key are rejected — the writer turns that rejection into "return the existing
        // record" (see EfAuditTrailWriter).
        builder.HasIndex(AuditSchema.ChainKey, nameof(AuditRecord.IdempotencyKey))
            .IsUnique()
                .HasDatabaseName(AuditSchema.IndexChainIdempotencyKey)
                    .HasFilter($"\"{AuditSchema.IdempotencyKey}\" IS NOT NULL");

        // GetActorActionsAsync's access pattern (WHERE tenant_id = @t AND actor_id = @a ORDER BY
        // occurred_on). TenantId is nullable, and Postgres B-tree indexes handle a NULL column value
        // like any other value for equality lookups (WHERE tenant_id IS NULL uses the index too), so
        // no separate "system" partial index is needed here.
        builder.HasIndex(x => new { x.TenantId, x.ActorId, x.OccurredOn })
            .HasDatabaseName(AuditSchema.IndexActorActions);

        // GetResourceHistoryAsync's access pattern: narrows one chain down to one resource instance.
        builder.HasIndex(AuditSchema.ChainKey, nameof(AuditRecord.ResourceId), nameof(AuditRecord.Sequence))
            .HasDatabaseName(AuditSchema.IndexChainResource);
    }
}
