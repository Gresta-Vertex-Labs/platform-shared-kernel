using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence.EfCore.Tests.TestFixtures;

// ---------------------------------------------------------------------------
// Main test DbContext (single-tenant)
// ---------------------------------------------------------------------------

/// <summary>
/// Test DbContext scoped to TestAggregate, AuditableTestAggregate, HardDeleteAggregate only.
/// Manually applies configurations to prevent TenantedTestAggregateConfig (in same test assembly)
/// from being picked up by ApplyConfigurationsFromAssembly.
/// </summary>
public sealed class TestDbContext : SharedKernelDbContext
{
    public DbSet<TestAggregate> TestAggregates => Set<TestAggregate>();
    public DbSet<AuditableTestAggregate> AuditableAggregates => Set<AuditableTestAggregate>();
    public DbSet<HardDeleteAggregate> HardDeleteAggregates => Set<HardDeleteAggregate>();
    public DbSet<ConcurrentTestAggregate> ConcurrentAggregates => Set<ConcurrentTestAggregate>();
    public DbSet<KeysetTestAggregate> KeysetAggregates => Set<KeysetTestAggregate>();

    public TestDbContext(DbContextOptions<TestDbContext> options, PersistenceContextDependencies dependencies)
        : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Manually apply only the configurations for this context's entities.
        // We do NOT call base.OnModelCreating because SharedKernelDbContext's implementation
        // scans GetType().Assembly and would pick up TenantedTestAggregateConfig.
        modelBuilder.ApplyConfiguration(new TestAggregateConfig());
        modelBuilder.ApplyConfiguration(new AuditableTestAggregateConfig());
        modelBuilder.ApplyConfiguration(new HardDeleteAggregateConfig());
        modelBuilder.ApplyConfiguration(new ConcurrentTestAggregateConfig());
        modelBuilder.ApplyConfiguration(new KeysetTestAggregateConfig());
    }
}

// ---------------------------------------------------------------------------
// Entity configurations
// ---------------------------------------------------------------------------

public sealed class TestAggregateConfig : IEntityTypeConfiguration<TestAggregate>
{
    public void Configure(EntityTypeBuilder<TestAggregate> builder)
    {
        builder.HasKey("Id");
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
    }
}

public sealed class AuditableTestAggregateConfig : IEntityTypeConfiguration<AuditableTestAggregate>
{
    public void Configure(EntityTypeBuilder<AuditableTestAggregate> builder)
    {
        builder.HasKey("Id");
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
    }
}

public sealed class HardDeleteAggregateConfig : IEntityTypeConfiguration<HardDeleteAggregate>
{
    public void Configure(EntityTypeBuilder<HardDeleteAggregate> builder)
    {
        builder.HasKey("Id");
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
    }
}

public sealed class ConcurrentTestAggregateConfig : IEntityTypeConfiguration<ConcurrentTestAggregate>
{
    public void Configure(EntityTypeBuilder<ConcurrentTestAggregate> builder)
    {
        builder.HasKey("Id");
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
    }
}

public sealed class KeysetTestAggregateConfig : IEntityTypeConfiguration<KeysetTestAggregate>
{
    public void Configure(EntityTypeBuilder<KeysetTestAggregate> builder)
    {
        builder.HasKey("Id");
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.SequenceNumber).IsRequired();
    }
}

// ---------------------------------------------------------------------------
// Tenanted test DbContext
// ---------------------------------------------------------------------------

/// <summary>
/// Test DbContext scoped to TenantedTestAggregate only.
/// Its global tenant query filter is driven by IRequestContext.TenantId.
/// </summary>
public sealed class TenantedTestDbContext : TenantedDbContext
{
    public DbSet<TenantedTestAggregate> TenantedAggregates => Set<TenantedTestAggregate>();

    public TenantedTestDbContext(DbContextOptions<TenantedTestDbContext> options, PersistenceContextDependencies dependencies)
        : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply only this context's entity configurations without the full assembly scan.
        // We do NOT call base.OnModelCreating to avoid picking up configs from other test contexts.
        modelBuilder.ApplyConfiguration(new TenantedTestAggregateConfig());
    }
}

public sealed class TenantedTestAggregateConfig : IEntityTypeConfiguration<TenantedTestAggregate>
{
    public void Configure(EntityTypeBuilder<TenantedTestAggregate> builder)
    {
        builder.HasKey("Id");
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
    }
}

// ---------------------------------------------------------------------------
// DbContext for soft-deletable tenanted aggregate (TenantedRepository tests)
// ---------------------------------------------------------------------------

/// <summary>
/// Test DbContext for <see cref="SoftDeletableTenantedAggregate"/> — used by T-28 tests.
/// </summary>
public sealed class SoftDeletableTenantedDbContext : TenantedDbContext
{
    public DbSet<SoftDeletableTenantedAggregate> SdAggregates => Set<SoftDeletableTenantedAggregate>();

    public SoftDeletableTenantedDbContext(DbContextOptions<SoftDeletableTenantedDbContext> options, PersistenceContextDependencies dependencies)
        : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SoftDeletableTenantedAggregateConfig());
    }
}

public sealed class SoftDeletableTenantedAggregateConfig
    : IEntityTypeConfiguration<SoftDeletableTenantedAggregate>
{
    public void Configure(EntityTypeBuilder<SoftDeletableTenantedAggregate> builder)
    {
        builder.HasKey("Id");
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
    }
}
