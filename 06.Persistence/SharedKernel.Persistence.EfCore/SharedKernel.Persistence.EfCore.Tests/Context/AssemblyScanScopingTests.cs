using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;

namespace SharedKernel.Persistence.EfCore.Tests.Context;

// Two SharedKernelDbContext subclasses declared in the SAME test assembly, each
// relying entirely on the BASE OnModelCreating's now-scoped ApplyConfigurationsFromAssembly — neither
// overrides OnModelCreating at all, unlike SharedKernel.Persistence.EfCore.Tests.TestFixtures.TestDbContext/
// TenantedTestDbContext, which predate this fix and still opt out manually (that workaround is left in
// place; it is still correct, just no longer the only way to avoid the bleed). Proves a
// IEntityTypeConfiguration<T> for an entity type only the OTHER context exposes never reaches this
// context's model, purely from ApplyConfigurationsFromAssembly's own predicate — no manual filtering.

/// <summary>Entity exposed only by <see cref="ScopeWidgetDbContext"/>.</summary>
public sealed class ScopeWidget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Entity exposed only by <see cref="ScopeGadgetDbContext"/>.</summary>
public sealed class ScopeGadget
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class ScopeWidgetConfig : IEntityTypeConfiguration<ScopeWidget>
{
    public void Configure(EntityTypeBuilder<ScopeWidget> builder)
    {
        builder.ToTable("scope_widget");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(123).IsRequired();
    }
}

public sealed class ScopeGadgetConfig : IEntityTypeConfiguration<ScopeGadget>
{
    public void Configure(EntityTypeBuilder<ScopeGadget> builder)
    {
        builder.ToTable("scope_gadget");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Label).HasMaxLength(456).IsRequired();
    }
}

/// <summary>Exposes only <see cref="ScopeWidget"/> — no override of <c>OnModelCreating</c> at all.</summary>
public sealed class ScopeWidgetDbContext(DbContextOptions<ScopeWidgetDbContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    public DbSet<ScopeWidget> Widgets => Set<ScopeWidget>();
}

/// <summary>Exposes only <see cref="ScopeGadget"/> — no override of <c>OnModelCreating</c> at all.</summary>
public sealed class ScopeGadgetDbContext(DbContextOptions<ScopeGadgetDbContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    public DbSet<ScopeGadget> Gadgets => Set<ScopeGadget>();
}

public sealed class AssemblyScanScopingTests
{
    private static PersistenceContextDependencies BuildDependencies()
    {
        var actorContext = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        return new PersistenceContextDependencies(
            new AuditInterceptor(actorContext, clock), new SoftDeleteInterceptor(actorContext, clock), new ConcurrencyInterceptor());
    }

    private static DbContextOptions<TContext> BuildOptions<TContext>()
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                    .Options;

    [Fact]
    public void WidgetContext_Model_DoesNotIncludeGadget_EvenThoughItsConfigLivesInTheSameAssembly()
    {
        var dependencies = BuildDependencies();
        using var ctx = new ScopeWidgetDbContext(BuildOptions<ScopeWidgetDbContext>(), dependencies);

        var entityClrTypes = ctx.Model.GetEntityTypes().Select(e => e.ClrType).ToList();

        entityClrTypes.Should().Contain(typeof(ScopeWidget));
        entityClrTypes.Should().NotContain(typeof(ScopeGadget));
    }

    [Fact]
    public void GadgetContext_Model_DoesNotIncludeWidget_EvenThoughItsConfigLivesInTheSameAssembly()
    {
        var dependencies = BuildDependencies();
        using var ctx = new ScopeGadgetDbContext(BuildOptions<ScopeGadgetDbContext>(), dependencies);

        var entityClrTypes = ctx.Model.GetEntityTypes().Select(e => e.ClrType).ToList();

        entityClrTypes.Should().Contain(typeof(ScopeGadget));
        entityClrTypes.Should().NotContain(typeof(ScopeWidget));
    }

    [Fact]
    public void WidgetContext_StillAppliesItsOwnConfiguration()
    {
        // The scoping predicate must not ALSO suppress a context's own legitimate configuration.
        var dependencies = BuildDependencies();
        using var ctx = new ScopeWidgetDbContext(BuildOptions<ScopeWidgetDbContext>(), dependencies);

        var nameProperty = ctx.Model.FindEntityType(typeof(ScopeWidget))!.FindProperty(nameof(ScopeWidget.Name))!;

        nameProperty.GetMaxLength().Should().Be(123);
    }

    [Fact]
    public void GadgetContext_StillAppliesItsOwnConfiguration()
    {
        var dependencies = BuildDependencies();
        using var ctx = new ScopeGadgetDbContext(BuildOptions<ScopeGadgetDbContext>(), dependencies);

        var labelProperty = ctx.Model.FindEntityType(typeof(ScopeGadget))!.FindProperty(nameof(ScopeGadget.Label))!;

        labelProperty.GetMaxLength().Should().Be(456);
    }
}
