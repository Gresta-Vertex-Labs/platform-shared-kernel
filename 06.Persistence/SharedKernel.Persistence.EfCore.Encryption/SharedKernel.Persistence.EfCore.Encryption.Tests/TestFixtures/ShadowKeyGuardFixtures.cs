using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;

// A SEPARATE, minimal aggregate/DbContext pair used ONLY by EncryptionOwnedTypeShadowKeyGuardTests, to prove
// EncryptionModelConvention's model-build-time shadow-key guard fires for a same-table owned entity type's
// '.Encrypt(...)' property. Deliberately NOT configured via a discoverable IEntityTypeConfiguration<T> class:
// SharedKernelDbContext.OnModelCreating calls ModelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly),
// which would apply ANY public IEntityTypeConfiguration<T> found ANYWHERE in this test assembly to EVERY
// DbContext built from it — including EncryptionTestDbContext, breaking every other test in this project. The
// configuration below is applied via an inline Entity<T>(Action<...>) call instead, so it is reachable only from
// ShadowKeyGuardDbContext itself.

public sealed record ShadowKeyAggregateId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static ShadowKeyAggregateId New() => new(Guid.NewGuid());
}

/// <summary>An owned value with a property this fixture deliberately tries to '.Encrypt(...)' on a same-table owned entity type.</summary>
public sealed class ShadowKeyOwnedValue
{
    public string Secret { get; private set; } = string.Empty;

    public ShadowKeyOwnedValue(string secret) => Secret = secret;

    private ShadowKeyOwnedValue() { } // ORM path
}

public sealed class ShadowKeyAggregate : TenantedFullAuditableAggregateRoot<ShadowKeyAggregateId>
{
    public ShadowKeyOwnedValue Owned { get; private set; } = null!;

    public ShadowKeyAggregate(ShadowKeyAggregateId id, Guid tenantId, IClock clock, ShadowKeyOwnedValue owned)
        : base(id, tenantId, clock) => Owned = owned;

    private ShadowKeyAggregate() { } // ORM path

    protected override void OnDelete() { }
}

public sealed class ShadowKeyGuardDbContext : TenantedDbContext
{
    public DbSet<ShadowKeyAggregate> Aggregates => Set<ShadowKeyAggregate>();

    public ShadowKeyGuardDbContext(
        DbContextOptions<ShadowKeyGuardDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<ShadowKeyAggregateId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // installs the tenant filter convention — but ALSO runs the assembly-wide
        // ApplyConfigurationsFromAssembly scan, which (being assembly-wide, not context-scoped) picks up
        // EncCustomerConfig too, since it is a public IEntityTypeConfiguration<EncCustomer> living in this SAME
        // test assembly. This context never registers EncCustomerId's strongly-typed-id converter (it has no
        // reason to — EncCustomer is not one of ITS entities), so leaving EncCustomer in the model here would
        // fail this test for an unrelated reason (EncCustomerId's unconverted provider type). Explicitly drop it.
        modelBuilder.Ignore<EncCustomer>();

        modelBuilder.Entity<ShadowKeyAggregate>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.TenantId).IsRequired();
            // Mirrors EntityTypeConfigurationBase<TEntity,TId>.Configure's concurrency-token/audit-column wiring
            // (never reused directly — see this file's top remarks on why no IEntityTypeConfiguration<T> class
            // exists for this fixture) — enough for TenantedFullAuditableAggregateRoot's interfaces to model-build
            // cleanly, since this test's only interest is the OwnsOne '.Encrypt(...)' guard below.
            builder.Property(x => x.RowVersion).IsConcurrencyToken();
            builder.Property(x => x.CreatedBy).HasMaxLength(256).IsRequired();
            builder.Property(x => x.CreatedOn).IsRequired();
            builder.Property(x => x.ModifiedBy).HasMaxLength(256).IsRequired(false);
            builder.Property(x => x.ModifiedOn).IsRequired(false);

            builder.OwnsOne(x => x.Owned, a =>
            {
                // Deliberately NOT pinning a real CLR-backed key on the owned type — the point of this fixture is
                // the DEFAULT, always-shadow same-table owned-entity-type primary key that the model-build guard
                // in EncryptionModelConvention.ValidateRotationKeyShape must reject.
                a.Property(o => o.Secret).HasMaxLength(256).IsRequired().Encrypt("shadowkey.owned.secret");
            });
        });
    }
}
