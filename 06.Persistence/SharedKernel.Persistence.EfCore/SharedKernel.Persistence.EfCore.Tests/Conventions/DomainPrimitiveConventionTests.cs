using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conventions;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Conventions;

// ---------------------------------------------------------------------------
// T-10 test entities and support types
// ---------------------------------------------------------------------------

internal sealed record ConventionTestId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static ConventionTestId New() => new(Guid.NewGuid());
}

/// <summary>
/// Minimal value object for convention tests.
/// Implements <see cref="IValueObject"/> directly (not via <c>ValueObject</c> abstract base)
/// so that EF Core's constructor-binding convention can match parameters to mapped scalar properties
/// without triggering the abstract base's validation hook.
/// </summary>
internal sealed class MoneyValueObject : IValueObject
{
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    /// <summary>EF Core materialisation constructor — parameter names match property names (case-insensitive).</summary>
    public MoneyValueObject(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    // Parameterless constructor for EF Core shadow-property materialisation path.
    private MoneyValueObject() { }
}

/// <summary>Entity with an <see cref="IValueObject"/> property — verifies <c>OwnsOne</c> auto-apply.</summary>
internal class EntityWithValueObject : AggregateRoot<ConventionTestId>
{
    public string Title { get; private set; } = string.Empty;
    public MoneyValueObject? Price { get; private set; }

    public EntityWithValueObject(ConventionTestId id, string title, MoneyValueObject? price, IClock clock)
        : base(id, clock)
    {
        Title = title;
        Price = price;
    }

    protected EntityWithValueObject() { } // ORM
}

/// <summary>Simple aggregate without <see cref="IValueObject"/> properties — baseline for convention tests.</summary>
internal class SimpleConventionEntity : AggregateRoot<ConventionTestId>
{
    public string Name { get; private set; } = string.Empty;

    public SimpleConventionEntity(ConventionTestId id, string name, IClock clock) : base(id, clock)
    {
        Name = name;
    }

    protected SimpleConventionEntity() { }
}

/// <summary>Entity implementing IHasConcurrency for concurrency token convention tests.</summary>
internal class FullAuditConventionEntity : FullAuditableAggregateRoot<ConventionTestId>
{
    public string Name { get; private set; } = string.Empty;

    public FullAuditConventionEntity(ConventionTestId id, string name, IClock clock) : base(id, clock)
    {
        Name = name;
    }

    protected FullAuditConventionEntity() { }

    protected override void OnDelete() { }
}

// ---------------------------------------------------------------------------
// Convention-aware test DbContexts
// ---------------------------------------------------------------------------

internal sealed class ValueObjectConventionDbContext : SharedKernelDbContext
{
    public DbSet<EntityWithValueObject> Entities => Set<EntityWithValueObject>();

    public ValueObjectConventionDbContext(
        DbContextOptions<ValueObjectConventionDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency)
        : base(options, audit, softDelete, concurrency) { }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<ConventionTestId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new EntityWithValueObjectConfig());
        // Apply ValueObjectOwnershipBuilder after all entity configurations.
        ValueObjectOwnershipBuilder.Apply(modelBuilder);
    }
}

internal sealed class EntityWithValueObjectConfig : EntityTypeConfigurationBase<EntityWithValueObject, ConventionTestId>
{
    public override void Configure(EntityTypeBuilder<EntityWithValueObject> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        // Price (MoneyValueObject) — ValueObjectOwnershipBuilder.Apply() will auto-configure OwnsOne.
    }
}

internal sealed class SimpleConventionDbContext : SharedKernelDbContext
{
    public DbSet<SimpleConventionEntity> Entities => Set<SimpleConventionEntity>();

    public SimpleConventionDbContext(
        DbContextOptions<SimpleConventionDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency)
        : base(options, audit, softDelete, concurrency) { }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<ConventionTestId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SimpleConventionEntityConfig());
    }
}

internal sealed class SimpleConventionEntityConfig : EntityTypeConfigurationBase<SimpleConventionEntity, ConventionTestId>
{
    public override void Configure(EntityTypeBuilder<SimpleConventionEntity> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
    }
}

internal sealed class FullAuditConventionDbContext : SharedKernelDbContext
{
    public DbSet<FullAuditConventionEntity> Entities => Set<FullAuditConventionEntity>();

    public FullAuditConventionDbContext(
        DbContextOptions<FullAuditConventionDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency)
        : base(options, audit, softDelete, concurrency) { }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<ConventionTestId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new FullAuditConventionEntityConfig());
    }
}

internal sealed class FullAuditConventionEntityConfig : EntityTypeConfigurationBase<FullAuditConventionEntity, ConventionTestId>
{
    public override void Configure(EntityTypeBuilder<FullAuditConventionEntity> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
    }
}

// ---------------------------------------------------------------------------
// T-10 tests
// ---------------------------------------------------------------------------

/// <summary>
/// EF Core domain primitive convention tests (T-10 / WO-008 P-033).
/// </summary>
public sealed class DomainPrimitiveConventionTests
{
    private static ValueObjectConventionDbContext CreateValueObjectContext()
    {
        var options = new DbContextOptionsBuilder<ValueObjectConventionDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();
        var ctx = new ValueObjectConventionDbContext(
            options,
            new AuditInterceptor(userCtx, clock, svcOpts),
            new SoftDeleteInterceptor(userCtx, clock, svcOpts),
            new ConcurrencyInterceptor());
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static SimpleConventionDbContext CreateSimpleContext()
    {
        var options = new DbContextOptionsBuilder<SimpleConventionDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();
        var ctx = new SimpleConventionDbContext(
            options,
            new AuditInterceptor(userCtx, clock, svcOpts),
            new SoftDeleteInterceptor(userCtx, clock, svcOpts),
            new ConcurrencyInterceptor());
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static FullAuditConventionDbContext CreateFullAuditContext()
    {
        var options = new DbContextOptionsBuilder<FullAuditConventionDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();
        var ctx = new FullAuditConventionDbContext(
            options,
            new AuditInterceptor(userCtx, clock, svcOpts),
            new SoftDeleteInterceptor(userCtx, clock, svcOpts),
            new ConcurrencyInterceptor());
        ctx.Database.EnsureCreated();
        return ctx;
    }

    // -----------------------------------------------------------------------
    // ValueObjectOwnershipBuilder (renamed from ValueObjectOwnershipConvention — P-102)
    // -----------------------------------------------------------------------

    [Fact]
    public void ValueObjectOwnershipConvention_DoesNotExist_In_Assembly()
    {
        // T-30: P-102 renames ValueObjectOwnershipConvention to ValueObjectOwnershipBuilder.
        var assembly = typeof(SharedKernel.Persistence.EfCore.Conventions.ValueObjectOwnershipBuilder).Assembly;
        var oldType = assembly.GetTypes().FirstOrDefault(t => t.Name == "ValueObjectOwnershipConvention");
        oldType.Should().BeNull(
            "ValueObjectOwnershipConvention was renamed to ValueObjectOwnershipBuilder (P-102). " +
            "The old class name must not exist in the assembly.");
    }

    [Fact]
    public void ValueObjectOwnershipBuilder_Exists_In_Assembly()
    {
        var assembly = typeof(SharedKernel.Persistence.EfCore.Conventions.ValueObjectOwnershipBuilder).Assembly;
        var newType = assembly.GetTypes().FirstOrDefault(t => t.Name == "ValueObjectOwnershipBuilder");
        newType.Should().NotBeNull("ValueObjectOwnershipBuilder must exist in the EfCore assembly (P-102 rename)");
        newType!.IsAbstract.Should().BeTrue("ValueObjectOwnershipBuilder is a static class (sealed + abstract in IL)");
    }

    [Fact]
    public void ValueObjectOwnershipBuilder_AutoApplies_OwnsOne_ForIValueObjectProperty()
    {
        using var ctx = CreateValueObjectContext();

        var entityType = ctx.Model.FindEntityType(typeof(EntityWithValueObject));
        entityType.Should().NotBeNull();

        var priceNav = entityType!.FindNavigation(nameof(EntityWithValueObject.Price));
        priceNav.Should().NotBeNull("ValueObjectOwnershipBuilder.Apply() should configure OwnsOne for IValueObject properties");
        priceNav!.ForeignKey.IsOwnership.Should().BeTrue("Price is an owned value object");
    }

    [Fact]
    public void ValueObjectOwnershipBuilder_OwnedType_RegisteredAsOwned()
    {
        using var ctx = CreateValueObjectContext();

        var ownedType = ctx.Model.FindEntityType(typeof(MoneyValueObject));
        ownedType.Should().NotBeNull("MoneyValueObject should be registered as an owned entity type");
        ownedType!.IsOwned().Should().BeTrue();
    }

    [Fact]
    public void ValueObjectOwnershipBuilder_DoesNotAffect_NonValueObjectProperties()
    {
        using var ctx = CreateSimpleContext();

        var entityType = ctx.Model.FindEntityType(typeof(SimpleConventionEntity));
        entityType.Should().NotBeNull();

        var ownedNavigations = entityType!.GetNavigations()
            .Where(n => n.ForeignKey.IsOwnership)
            .ToList();
        ownedNavigations.Should().BeEmpty("No IValueObject properties on SimpleConventionEntity");
    }

    // -----------------------------------------------------------------------
    // Soft-delete global filter convention
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SoftDeleteConvention_AutoApplies_GlobalQueryFilter_ForISoftDeletableEntities()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();

        var id = TestId.New();
        ctx.AuditableAggregates.Add(new AuditableTestAggregate(id, "SoftDeleteTest", new SystemClock()));
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        ctx.AuditableAggregates.Remove((await ctx.AuditableAggregates.FindAsync(id))!);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var withFilter = await ctx.AuditableAggregates.CountAsync();
        var withoutFilter = await ctx.AuditableAggregates.IgnoreQueryFilters().CountAsync();

        withFilter.Should().Be(0, "soft-deleted entity excluded by global filter");
        withoutFilter.Should().Be(1, "soft-deleted entity visible when filter bypassed");
    }

    [Fact]
    public void SoftDeleteConvention_DoesNotApply_ToNonSoftDeletableEntities()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();

        var entityType = ctx.Model.FindEntityType(typeof(HardDeleteAggregate));
        entityType.Should().NotBeNull();

        var filters = entityType!.GetDeclaredQueryFilters();
        filters.Should().BeEmpty("non-ISoftDeletable entities must not have a soft-delete filter");
    }

    // -----------------------------------------------------------------------
    // Concurrency token convention
    // -----------------------------------------------------------------------

    [Fact]
    public void ConcurrencyTokenConvention_AppliedByEntityTypeConfigurationBase_ForIHasConcurrencyEntities()
    {
        // FullAuditConventionEntity extends FullAuditableAggregateRoot which implements IHasConcurrency.
        using var ctx = CreateFullAuditContext();

        var entityType = ctx.Model.FindEntityType(typeof(FullAuditConventionEntity));
        entityType.Should().NotBeNull();

        var concurrencyProp = entityType!.GetProperties().FirstOrDefault(p => p.IsConcurrencyToken);
        concurrencyProp.Should().NotBeNull("IHasConcurrency entities must have a concurrency token property configured");
    }

    [Fact]
    public void ConcurrencyTokenConvention_NotApplied_ToNonIHasConcurrencyEntities()
    {
        // TestAggregate does not implement IHasConcurrency.
        using var ctx = TestDbContextFactory.CreateTestDbContext();

        var entityType = ctx.Model.FindEntityType(typeof(TestAggregate));
        entityType.Should().NotBeNull();

        var concurrencyProp = entityType!.GetProperties().FirstOrDefault(p => p.IsConcurrencyToken);
        concurrencyProp.Should().BeNull("non-IHasConcurrency entities must not have a concurrency token");
    }

    // -----------------------------------------------------------------------
    // Tenant filter convention — opt-in only
    // -----------------------------------------------------------------------

    [Fact]
    public void TenantFilterConvention_IsNotApplied_ToNonTenantedEntities()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();

        var entityType = ctx.Model.FindEntityType(typeof(TestAggregate));
        entityType.Should().NotBeNull();

        var filters = entityType!.GetDeclaredQueryFilters();
        filters.Should().BeEmpty("non-IHasTenant entities in a single-tenant context must not have a tenant filter");
    }

    [Fact]
    public void TenantFilterConvention_IsApplied_WhenTenantedDbContextUsed()
    {
        using var ctx = TestDbContextFactory.CreateTenantedDbContext();

        var entityType = ctx.Model.FindEntityType(typeof(TenantedTestAggregate));
        entityType.Should().NotBeNull();

        var filters = entityType!.GetDeclaredQueryFilters();
        filters.Should().NotBeEmpty("IHasTenant entities in TenantedDbContext must have a tenant isolation filter");
    }
}
