using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Persistence.Abstractions.Auditing;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// EF Core entity type configuration for <see cref="AuditRecord"/>.
/// </summary>
/// <remarks>
/// <para>
/// WO-071/P-457/D-121. Deliberately implements <see cref="IEntityTypeConfiguration{TEntity}"/>
/// directly rather than extending <c>EntityTypeConfigurationBase&lt;TEntity,TId&gt;</c> —
/// <see cref="AuditRecord"/> is a plain infrastructure record, not an aggregate root, and carries
/// none of that base's marker interfaces (<c>IHasConcurrency</c>, <c>ISoftDeletable</c>,
/// <c>IHasCreatedAudit</c>/<c>IHasAudit</c>, <c>IHasTenant</c>) — see <see cref="AuditRecord"/>'s
/// remarks (D-120) for why.
/// </para>
/// <para>
/// Applied only when <c>EfCorePersistenceBuilder{TContext}.WithAuditTrail()</c> has been called —
/// see <see cref="SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext"/>'s
/// <c>auditTrailEnabled</c> constructor parameter.
/// </para>
/// </remarks>
public sealed class AuditRecordEntityConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    /// <summary>Hex SHA-256 digest length (32 bytes → 64 hex characters).</summary>
    private const int HashColumnLength = 64;

    /// <summary>
    /// Matches <c>AuditInterceptor</c>'s existing audit-string-format column length (P-091) —
    /// sufficient for a 36-character GUID string or a short service-name fallback.
    /// </summary>
    private const int ActorIdColumnLength = 256;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantId).IsRequired();

        builder.Property(x => x.ActorId).IsRequired().HasMaxLength(ActorIdColumnLength);
        builder.Property(x => x.Action).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ResourceType).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ResourceId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.OccurredOn).IsRequired();

        // D-121: opaque snapshot columns — plain text, NEVER jsonb. This package must never
        // parse or validate caller-supplied snapshot content.
        builder.Property(x => x.BeforeSnapshot).HasColumnType("text");
        builder.Property(x => x.AfterSnapshot).HasColumnType("text");

        builder.Property(x => x.CorrelationId).HasMaxLength(ActorIdColumnLength);
        builder.Property(x => x.ApprovalId).HasMaxLength(ActorIdColumnLength);

        builder.Property(x => x.RecordHash).IsRequired().HasMaxLength(HashColumnLength);
        builder.Property(x => x.PreviousRecordHash).HasMaxLength(HashColumnLength);

        // D-121: composite indexes for the two named access patterns
        // (IAuditQueryService.GetResourceHistoryAsync / GetActorActionsAsync).
        builder.HasIndex(x => new { x.TenantId, x.ResourceType, x.ResourceId, x.OccurredOn })
            .HasDatabaseName("ix_audit_records_resource_history");

        builder.HasIndex(x => new { x.TenantId, x.ActorId, x.OccurredOn })
            .HasDatabaseName("ix_audit_records_actor_actions");
    }
}
