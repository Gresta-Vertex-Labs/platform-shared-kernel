using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Interceptors;
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

// ---------------------------------------------------------------------------
// Tenanted test DbContext
// ---------------------------------------------------------------------------

/// <summary>
/// Test DbContext scoped to TenantedTestAggregate only.
/// Manually applies configurations and tenant filter.
/// </summary>
public sealed class TenantedTestDbContext : TenantedDbContext
{
    public DbSet<TenantedTestAggregate> TenantedAggregates => Set<TenantedTestAggregate>();

    public TenantedTestDbContext(
        DbContextOptions<TenantedTestDbContext> options,
        AuditInterceptor auditInterceptor,
        SoftDeleteInterceptor softDeleteInterceptor,
        ConcurrencyInterceptor concurrencyInterceptor,
        ICurrentTenantService currentTenantService)
        : base(options, auditInterceptor, softDeleteInterceptor, concurrencyInterceptor, currentTenantService)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<TenantedTestId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply only this context's entity configurations.
        // Skip base.OnModelCreating to avoid assembly scan picking up non-tenanted configs.
        modelBuilder.ApplyConfiguration(new TenantedTestAggregateConfig());

        // Install tenant filter manually (replicate TenantedDbContext's logic for this entity).
        var tenantService = CurrentTenantService;
        modelBuilder.Entity<TenantedTestAggregate>()
            .HasQueryFilter(e => e.TenantId == tenantService.TenantId);
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
