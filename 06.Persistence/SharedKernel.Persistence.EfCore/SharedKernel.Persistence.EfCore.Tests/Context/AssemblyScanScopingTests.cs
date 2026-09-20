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

// ---------------------------------------------------------------------------
// Navigation-only child: reachable ONLY through ScopeParent.Children, no DbSet<T> of its own — the
// exact shape the scoped scan's original DbSet-only ExposedEntityTypes silently dropped a dedicated
// IEntityTypeConfiguration<T> for.
// ---------------------------------------------------------------------------

public sealed class ScopeNavigationChild
{
    public Guid Id { get; set; }
    public Guid ScopeParentWithChildId { get; set; }
    public string Detail { get; set; } = string.Empty;
}

public sealed class ScopeParentWithChild
{
    public Guid Id { get; set; }
    public List<ScopeNavigationChild> Children { get; } = [];
}

public sealed class ScopeParentWithChildConfig : IEntityTypeConfiguration<ScopeParentWithChild>
{
    public void Configure(EntityTypeBuilder<ScopeParentWithChild> builder)
    {
        builder.ToTable("scope_parent_with_child");
        builder.HasKey(x => x.Id);
        builder.HasMany(x => x.Children).WithOne().HasForeignKey(c => c.ScopeParentWithChildId);
    }
}

/// <summary>
/// The dedicated configuration for the navigation-only child — must be applied even though
/// <see cref="ScopeParentDbContext"/> exposes no <c>DbSet&lt;ScopeNavigationChild&gt;</c>.
/// </summary>
public sealed class ScopeNavigationChildConfig : IEntityTypeConfiguration<ScopeNavigationChild>
{
    public void Configure(EntityTypeBuilder<ScopeNavigationChild> builder)
    {
        builder.ToTable("scope_navigation_child");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Detail).HasMaxLength(789).IsRequired();
    }
}

/// <summary>
/// Exposes only <see cref="ScopeParentWithChild"/> via <c>DbSet&lt;T&gt;</c> — no override of
/// <c>OnModelCreating</c> at all, and deliberately no <c>AdditionalConfiguredEntityTypes</c> override
/// either, so <see cref="ScopeNavigationChild"/>'s configuration can ONLY be reached through the base
/// scoped scan's navigation-discovery fallback, never a manual opt-in.
/// </summary>
public sealed class ScopeParentDbContext(DbContextOptions<ScopeParentDbContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    public DbSet<ScopeParentWithChild> Parents => Set<ScopeParentWithChild>();
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

    [Fact]
    public void ParentContext_Model_IncludesTheNavigationOnlyChild_WithNoDbSetOrManualOptIn()
    {
        var dependencies = BuildDependencies();
        using var ctx = new ScopeParentDbContext(BuildOptions<ScopeParentDbContext>(), dependencies);

        var entityClrTypes = ctx.Model.GetEntityTypes().Select(e => e.ClrType).ToList();

        entityClrTypes.Should().Contain(typeof(ScopeParentWithChild));
        entityClrTypes.Should().Contain(
            typeof(ScopeNavigationChild),
            "a child reachable only through a navigation must still be part of the model — EF Core's " +
                "own navigation discovery reaches it independently of this context's DbSet<T> properties");
    }

    [Fact]
    public void ParentContext_StillAppliesTheNavigationOnlyChildsOwnConfiguration()
    {
        // The actual regression this suite closes: before the fix, ScopeNavigationChild WAS present in
        // the model (EF's own navigation discovery always added it) but its DEDICATED configuration —
        // column name, max length, and (in the real encryption scenario) a .Encrypt(...) annotation —
        // was silently never applied, because the scoped predicate rejected ScopeNavigationChildConfig
        // for not being a DbSet<T> on this context.
        var dependencies = BuildDependencies();
        using var ctx = new ScopeParentDbContext(BuildOptions<ScopeParentDbContext>(), dependencies);

        var childEntityType = ctx.Model.FindEntityType(typeof(ScopeNavigationChild));
        childEntityType.Should().NotBeNull();

        childEntityType!.GetTableName().Should().Be("scope_navigation_child");
        var detailProperty = childEntityType.FindProperty(nameof(ScopeNavigationChild.Detail));
        detailProperty.Should().NotBeNull();
        detailProperty!.GetMaxLength().Should().Be(
            789, "ScopeNavigationChildConfig must actually run, not merely leave the child present " +
                "in the model with EF's bare conventional defaults");
    }
}
