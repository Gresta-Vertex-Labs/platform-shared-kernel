using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;

// Used only by EncryptionRotationCheckpointIntegrationTests (H11 coverage): a SECOND encrypted entity type, whose
// name ("EncAaaOrder") sorts alphabetically BEFORE "EncCustomer" — EncryptionRotationService.BuildTargets orders
// entity types by name, so introducing this type shifts where EncCustomer's own targets land in that list. It is
// deliberately configured only via EncryptionTestDbContextV2 (never a discoverable IEntityTypeConfiguration<T>
// class — see that context's own remarks for why), so EncryptionTestDbContext (every OTHER test in this project)
// never sees it at all.

public sealed record EncAaaOrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static EncAaaOrderId New() => new(Guid.NewGuid());
}

public sealed class EncAaaOrder : TenantedFullAuditableAggregateRoot<EncAaaOrderId>
{
    public string Note { get; private set; } = string.Empty;

    public EncAaaOrder(EncAaaOrderId id, Guid tenantId, IClock clock, string note)
        : base(id, tenantId, clock) => Note = note;

    private EncAaaOrder() { } // ORM path

    protected override void OnDelete() { }
}

// Mirrors EntityTypeConfigurationBase<TEntity,TId>.Configure's concurrency-token/audit-column wiring by hand,
// applied inline from EncryptionTestDbContextV2.OnModelCreating rather than through a discoverable
// IEntityTypeConfiguration<T> — see that context's remarks.
internal static class EncAaaOrderModelBuilderExtensions
{
    public static void ConfigureEncAaaOrder(this EntityTypeBuilder<EncAaaOrder> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.Property(x => x.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CreatedOn).IsRequired();
        builder.Property(x => x.ModifiedBy).HasMaxLength(256).IsRequired(false);
        builder.Property(x => x.ModifiedOn).IsRequired(false);
        builder.Property(x => x.Note).HasMaxLength(1024).IsRequired().Encrypt("order.note");
    }
}
