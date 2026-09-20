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

// Used only by the purpose-uniqueness model-build-time guard coverage. Deliberately NOT configured via a
// discoverable IEntityTypeConfiguration<T> class — see ShadowKeyGuardFixtures.cs's remarks for why an
// assembly-wide ApplyConfigurationsFromAssembly scan would otherwise bleed this into EVERY other context in
// this test assembly.

public sealed record DuplicatePurposeAggregateId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static DuplicatePurposeAggregateId New() => new(Guid.NewGuid());
}

public sealed class DuplicatePurposeAggregate : TenantedFullAuditableAggregateRoot<DuplicatePurposeAggregateId>
{
    public string First { get; private set; } = string.Empty;

    public string Second { get; private set; } = string.Empty;

    public DuplicatePurposeAggregate(DuplicatePurposeAggregateId id, Guid tenantId, IClock clock, string first, string second)
        : base(id, tenantId, clock)
    {
        First = first;
        Second = second;
    }

    private DuplicatePurposeAggregate() { } // ORM path

    protected override void OnDelete() { }
}

public sealed class DuplicatePurposeDbContext : TenantedDbContext
{
    public DbSet<DuplicatePurposeAggregate> Aggregates => Set<DuplicatePurposeAggregate>();

    public DuplicatePurposeDbContext(
        DbContextOptions<DuplicatePurposeDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<DuplicatePurposeAggregateId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Ignore<EncCustomer>();
        modelBuilder.Ignore<ShadowKeyAggregate>();

        modelBuilder.Entity<DuplicatePurposeAggregate>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.TenantId).IsRequired();
            builder.Property(x => x.RowVersion).IsConcurrencyToken();
            builder.Property(x => x.CreatedBy).HasMaxLength(256).IsRequired();
            builder.Property(x => x.CreatedOn).IsRequired();
            builder.Property(x => x.ModifiedBy).HasMaxLength(256).IsRequired(false);
            builder.Property(x => x.ModifiedOn).IsRequired(false);

            // Both properties deliberately share ONE purpose — the exact shape ValidatePurposeUnique must reject:
            // same purpose, same owning row, so their AAD would be identical and their ciphertext freely swappable.
            builder.Property(x => x.First).HasMaxLength(256).IsRequired().Encrypt("duplicate.shared");
            builder.Property(x => x.Second).HasMaxLength(256).IsRequired().Encrypt("duplicate.shared");
        });
    }
}
