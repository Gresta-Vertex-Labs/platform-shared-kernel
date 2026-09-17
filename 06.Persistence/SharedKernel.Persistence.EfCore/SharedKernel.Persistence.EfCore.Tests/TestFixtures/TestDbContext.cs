using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Security.Abstractions;

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

    public TestDbContext(
        DbContextOptions<TestDbContext> options,
        AuditInterceptor auditInterceptor,
        SoftDeleteInterceptor softDeleteInterceptor,
        ConcurrencyInterceptor concurrencyInterceptor)
        : base(options, auditInterceptor, softDeleteInterceptor, concurrencyInterceptor)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<TestId, Guid>();
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

public sealed class TestAggregateConfig : EntityTypeConfigurationBase<TestAggregate, TestId>
{
    public override void Configure(EntityTypeBuilder<TestAggregate> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
    }
}

public sealed class AuditableTestAggregateConfig : EntityTypeConfigurationBase<AuditableTestAggregate, TestId>
{
    public override void Configure(EntityTypeBuilder<AuditableTestAggregate> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
    }
}

public sealed class HardDeleteAggregateConfig : EntityTypeConfigurationBase<HardDeleteAggregate, TestId>
{
    public override void Configure(EntityTypeBuilder<HardDeleteAggregate> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
    }
}

public sealed class ConcurrentTestAggregateConfig : EntityTypeConfigurationBase<ConcurrentTestAggregate, TestId>
{
    public override void Configure(EntityTypeBuilder<ConcurrentTestAggregate> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
    }
}

public sealed class KeysetTestAggregateConfig : EntityTypeConfigurationBase<KeysetTestAggregate, TestId>
{
    public override void Configure(EntityTypeBuilder<KeysetTestAggregate> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.SequenceNumber).IsRequired();
    }
}

// ---------------------------------------------------------------------------
// Tenanted test DbContext
// ---------------------------------------------------------------------------

/// <summary>
/// Test DbContext scoped to TenantedTestAggregate only.
/// Uses ITenantProvider (Security.Abstractions) to drive the global tenant query filter.
/// </summary>
public sealed class TenantedTestDbContext : TenantedDbContext
{
    public DbSet<TenantedTestAggregate> TenantedAggregates => Set<TenantedTestAggregate>();

    public TenantedTestDbContext(
        DbContextOptions<TenantedTestDbContext> options,
        AuditInterceptor auditInterceptor,
        SoftDeleteInterceptor softDeleteInterceptor,
        ConcurrencyInterceptor concurrencyInterceptor,
        ITenantProvider tenantProvider)
        : base(options, auditInterceptor, softDeleteInterceptor, concurrencyInterceptor, tenantProvider)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<TenantedTestId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply only this context's entity configurations without the full assembly scan.
        // We do NOT call base.OnModelCreating to avoid picking up configs from other test contexts.
        // ApplyTenantFilters installs the expression-tree tenant filter after entity configs.
        modelBuilder.ApplyConfiguration(new TenantedTestAggregateConfig());
        ApplyTenantFilters(modelBuilder);
    }
}

public sealed class TenantedTestAggregateConfig : EntityTypeConfigurationBase<TenantedTestAggregate, TenantedTestId>
{
    public override void Configure(EntityTypeBuilder<TenantedTestAggregate> builder)
    {
        base.Configure(builder);
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

    public SoftDeletableTenantedDbContext(
        DbContextOptions<SoftDeletableTenantedDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency,
        ITenantProvider tenantProvider)
        : base(options, audit, softDelete, concurrency, tenantProvider)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<TenantedTestId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SoftDeletableTenantedAggregateConfig());
        ApplyTenantFilters(modelBuilder);
    }
}

public sealed class SoftDeletableTenantedAggregateConfig
    : EntityTypeConfigurationBase<SoftDeletableTenantedAggregate, TenantedTestId>
{
    public override void Configure(EntityTypeBuilder<SoftDeletableTenantedAggregate> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
    }
}
