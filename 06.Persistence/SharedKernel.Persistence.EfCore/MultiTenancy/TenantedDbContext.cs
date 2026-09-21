using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// Abstract EF Core DbContext base for multi-tenant services.
/// Installs a runtime-captured, named global query filter on all root <see cref="IHasTenant"/>
/// entities so that every query is automatically scoped to the current tenant.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Filter semantics:</strong> the global filter is
/// <c>e.TenantId == CurrentTenantId</c> (a nullable comparison — see below), installed under
/// the named key <see cref="PersistenceFilterNames.Tenant"/> so it can never
/// silently replace — or be replaced by — the soft-delete filter
/// (<see cref="PersistenceFilterNames.SoftDelete"/>) that
/// <c>Conventions.SoftDeleteQueryFilterConvention</c> installs on the same entity types; EF Core
/// combines every named filter on an entity type with a logical AND. The value is resolved at
/// query-execution time, not at startup, so rotating the current tenant (e.g., across HTTP requests
/// in a scoped context) works correctly without rebuilding the context.
/// </para>
/// <para>
/// <strong>Fail-closed sentinel:</strong> when <see cref="CurrentTenantId"/> resolves
/// <see langword="null"/> (no tenant resolved), the filter matches zero rows and
/// the tenant write guard rejects every tenant-scoped write. With no
/// <c>IRequestContext</c> registered, the builder's default is <c>AnonymousRequestContext</c> —
/// no tenant — so a service that forgets to wire identity reads and writes nothing.
/// </para>
/// <para>
/// <strong>One caller seam:</strong> the tenant comes from the same
/// <see cref="SharedKernelDbContext.RequestContext"/> (<c>IRequestContext</c>, shared with
/// <c>05.Application</c>) that audit attribution reads. A service implements it once at its
/// composition root — <c>13.ServiceDefaults</c> ships an implementation over <c>12.Security</c>.
/// </para>
/// <para>
/// <strong>Pooling-safe:</strong> the constructor takes no identity at all; the request context is
/// attached per lease (<c>TenantAwareDbContextFactory</c>, the read-replica accessor) and reset to the
/// fail-closed anonymous context on <see cref="SharedKernelDbContext.Dispose"/>/
/// <see cref="SharedKernelDbContext.DisposeAsync"/> — the hook EF Core 10's pool return passes through.
/// </para>
/// <para>
/// <strong>Filter implementation:</strong> the global filter lambda is built using expression trees
/// (<c>Expression.Parameter</c>, <c>Expression.Property</c>, <c>Expression.Constant</c>,
/// <c>Expression.Equal</c>, <c>Expression.Lambda</c>) via the non-generic
/// <c>modelBuilder.Entity(clrType).HasQueryFilter(key, lambda)</c> overload. The filter binds through
/// <c>Expression.Constant(this, GetType())</c> → <see cref="CurrentTenantId"/> — a captured "this
/// DbContext instance" constant, rebound by EF Core's
/// query-filter compilation to whichever instance is EXECUTING the query, never the instance whose
/// <see cref="OnModelCreating"/> built the (process-wide-cached) model.
/// </para>
/// <para>
/// <strong>Root types only:</strong> a TPH-derived (non-root) entity type shares
/// its base type's table and cannot carry its own query filter — only entity types with
/// <c>BaseType == null</c> are considered.
/// </para>
/// <para>
/// Multi-tenant services must extend this class instead of <see cref="SharedKernelDbContext"/>.
/// Single-tenant services extend <see cref="SharedKernelDbContext"/> directly.
/// </para>
/// <para>
/// <strong>Cross-tenant access:</strong> admin or migration paths that need to bypass the filter
/// must enter an <see cref="ICrossTenantScope"/> explicitly (<c>ICrossTenantScope.Enter(reason)</c>), then
/// call <c>.IgnoreQueryFilters([PersistenceFilterNames.Tenant])</c> — never a bare
/// <c>.IgnoreQueryFilters()</c>, which would also drop the soft-delete filter — or use
/// <c>TenantedRepository&lt;T,TId&gt;.GetByIdForTenantAsync</c>, which now enforces the same
/// active-scope requirement.
/// </para>
/// </remarks>
public abstract class TenantedDbContext : SharedKernelDbContext
{
    // Model-build-time-only reflection lookup for the public CurrentTenantId property declared on
    // this class — never invoked in a query hot path.
    private static readonly System.Reflection.PropertyInfo CurrentTenantIdPropertyInfo =
        typeof(TenantedDbContext).GetProperty(nameof(CurrentTenantId))!;

    /// <summary>
    /// Initialises a new <see cref="TenantedDbContext"/>.
    /// </summary>
    /// <param name="options">EF Core context options supplied by the DI container.</param>
    /// <param name="dependencies">
    /// Every interceptor/convention/configurator/options-extension/exception-classifier dependency this
    /// context needs, bundled into one required parameter — see
    /// <see cref="PersistenceContextDependencies"/>. A derived context MUST declare exactly
    /// <c>MyContext(DbContextOptions&lt;MyContext&gt; options, PersistenceContextDependencies dependencies)
    /// : base(options, dependencies)</c> and forward both parameters unchanged.
    /// </param>
    protected TenantedDbContext(DbContextOptions options, PersistenceContextDependencies dependencies)
        : base(options, dependencies)
    {
    }

    /// <summary>
    /// Gets the tenant the tenant global query filter and the tenant write guard scope to:
    /// <see cref="SharedKernelDbContext.RequestContext"/>'s <c>TenantId</c>, or <see langword="null"/>
    /// when no tenant is resolved (fail closed).
    /// </summary>
    public Guid? CurrentTenantId => RequestContext.TenantId;

    /// <summary>
    /// Applies the tenant global query filter in addition to the base configurations.
    /// </summary>
    /// <param name="modelBuilder">The builder used to construct the model for this context.</param>
    /// <remarks>
    /// Downstream contexts that override this method must call
    /// <c>base.OnModelCreating(modelBuilder)</c> first to ensure all conventions and tenant
    /// filters are applied. Subclasses that perform their own entity configuration without an
    /// assembly scan should call <c>base.OnModelCreating(modelBuilder)</c> to install tenant
    /// filters, then manually apply only their own configurations.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyTenantFilters(modelBuilder);
    }

    /// <summary>
    /// Installs the expression-tree tenant query filter on all root <see cref="IHasTenant"/> entity
    /// types currently registered in the model. Call this after all entity configurations are applied.
    /// </summary>
    /// <param name="modelBuilder">The builder used to construct the model for this context.</param>
    /// <remarks>
    /// <para>
    /// Subclasses that bypass the assembly-scan path in <c>OnModelCreating</c> should call this
    /// method explicitly after applying their entity configurations to ensure tenant isolation is
    /// preserved.
    /// </para>
    /// <para>
    /// <strong>Write-side protection:</strong> in addition to the query filter, this method marks
    /// <c>TenantId</c> as an EF Core concurrency token on every entity type it visits — see
    /// <see cref="ApplyTenantConcurrencyToken"/>.
    /// </para>
    /// </remarks>
    protected void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.BaseType is not null)
                continue; // TPH-derived type — shares its root's filter.

            if (!typeof(IHasTenant).IsAssignableFrom(entityType.ClrType))
                continue;

            ApplyTenantFilterExpression(modelBuilder, entityType.ClrType);
            ApplyTenantConcurrencyToken(modelBuilder, entityType.ClrType);
        }
    }

    // A detached IHasTenant entity re-attached via Update()/Remove() (EfRepository.
    // MarkAsModifiedIfDetached) is written with a WHERE clause naming only its primary key — TenantId
    // plays no part in it. The tenant write guard already rejects any entry whose IN-MEMORY
    // TenantId differs from the caller's current tenant, but that check cannot see which tenant the
    // TARGETED ROW actually belongs to: a caller who builds a detached stub carrying their OWN
    // (legitimate) TenantId and a VICTIM's primary key passes that check, and an unconditional
    // Update()/Remove() would then silently rewrite or delete the victim's row, because the physical
    // UPDATE/DELETE statement never mentions TenantId at all.
    //
    // Marking TenantId a concurrency token closes this: EF Core adds it to the UPDATE/DELETE WHERE
    // clause, compared against the OriginalValue captured on this entry. For a genuinely tracked
    // entity (loaded through the tenant query filter) OriginalValue already equals the row's real
    // TenantId, so this is a no-op. For a freshly-attached DETACHED entry, EF Core has no source for
    // "original" other than the value already on the object — the SAME value
    // tenant write guard already required to equal the caller's current tenant. The WHERE
    // clause therefore becomes "Id = <target> AND TenantId = <caller's own tenant>": a victim row
    // belonging to a DIFFERENT tenant matches zero rows, and EF Core raises
    // DbUpdateConcurrencyException instead of silently succeeding — translated by
    // the concurrency translator, which reads the row, sees it belongs to another tenant (proven) and
    // raises the same tenant-isolation Forbidden error the write guard raises proactively.
    private static void ApplyTenantConcurrencyToken(ModelBuilder modelBuilder, Type clrType) =>
        modelBuilder.Entity(clrType).Property(nameof(IHasTenant.TenantId)).IsConcurrencyToken();

    // Builds a lambda: e => this.CurrentTenantId.HasValue && e.TenantId == this.CurrentTenantId
    // using expression trees, where "this" is a captured DbContext-instance constant that EF Core
    // rebinds to whichever instance is executing the query. The HasValue guard makes the
    // fail-closed "no tenant resolved" behavior explicit rather than relying on a Guid==Guid?
    // comparison's implicit false-on-null.
    private void ApplyTenantFilterExpression(ModelBuilder modelBuilder, Type clrType)
    {
        // Parameter: e
        var param = Expression.Parameter(clrType, "e");

        // e.TenantId
        var tenantIdProperty = Expression.Property(param, nameof(IHasTenant.TenantId));

        // this — a captured "this DbContext instance" constant. EF Core's query-filter compilation
        // recognizes a ConstantExpression whose Value is the DbContext instance the model was built
        // from and rebinds it, per query execution, to the CURRENT executing instance.
        var thisConst = Expression.Constant(this, GetType());

        // this.CurrentTenantId (Guid?)
        var tenantIdAccess = Expression.Property(thisConst, CurrentTenantIdPropertyInfo);

        // this.CurrentTenantId.HasValue
        var hasValue = Expression.Property(tenantIdAccess, nameof(Nullable<Guid>.HasValue));

        // e.TenantId == this.CurrentTenantId (Guid promoted to Guid? for the comparison)
        var equalExpr = Expression.Equal(Expression.Convert(tenantIdProperty, typeof(Guid?)), tenantIdAccess);

        // this.CurrentTenantId.HasValue && e.TenantId == this.CurrentTenantId
        var guarded = Expression.AndAlso(hasValue, equalExpr);

        // e =>...
        var lambda = Expression.Lambda(guarded, param);

        // Apply via non-generic overload, named "Tenant" — no reflection on entity type needed.
        modelBuilder.Entity(clrType).HasQueryFilter(PersistenceFilterNames.Tenant, lambda);
    }
}
