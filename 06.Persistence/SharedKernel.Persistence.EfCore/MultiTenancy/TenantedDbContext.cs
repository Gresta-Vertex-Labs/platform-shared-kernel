using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Interceptors;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// Abstract EF Core DbContext base for multi-tenant services.
/// Installs a runtime-captured, named global query filter on all root <see cref="IHasTenant"/>
/// entities so that every query is automatically scoped to the current tenant.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Filter semantics:</strong> the global filter is
/// <c>e.TenantId == CurrentTenant.TenantId</c> (a nullable comparison — see below), installed under
/// the named key <see cref="PersistenceFilterNames.Tenant"/> so it can never
/// silently replace — or be replaced by — the soft-delete filter
/// (<see cref="PersistenceFilterNames.SoftDelete"/>) that
/// <c>Conventions.SoftDeleteQueryFilterConvention</c> installs on the same entity types; EF Core
/// combines every named filter on an entity type with a logical AND. The value is resolved at
/// query-execution time, not at startup, so rotating the current tenant (e.g., across HTTP requests
/// in a scoped context) works correctly without rebuilding the context.
/// </para>
/// <para>
/// <strong>Fail-closed sentinel:</strong> when <see cref="CurrentTenant"/>'s
/// <see cref="ICurrentTenantContext.TenantId"/> resolves <see langword="null"/> (no tenant
/// resolved), the filter matches zero rows. No production entity should carry a
/// <see langword="null"/>-equivalent tenant id — this is intentional, and prevents cross-tenant data
/// leaks when no real tenant context is registered.
/// </para>
/// <para>
/// <strong>Orthogonal to actor identity:</strong> tenant identity is resolved through its own seam,
/// <see cref="ICurrentTenantContext"/> — orthogonal to <see cref="SharedKernelDbContext.CurrentActor"/>
/// (<see cref="ICurrentActorContext"/>), which only ever carries actor identity now. A consuming
/// service bridges <see cref="ICurrentTenantContext"/> to its real tenant source (typically
/// <c>12.Security.Abstractions</c>'s <c>ITenantProvider</c>) at its own composition root —
/// <c>13.ServiceDefaults/SharedKernel.ServiceDefaults.Persistence</c> ships that bridge for services
/// that already use <c>12.Security</c>.
/// </para>
/// <para>
/// <strong>Pooling-safe:</strong> this class's
/// constructor takes NO <see cref="ICurrentTenantContext"/> dependency at all — <see cref="CurrentTenant"/>
/// starts as the fail-closed <see cref="MultiTenancy.NullCurrentTenantContext.Instance"/> and is
/// attached per lease by <see cref="RefreshTenant"/>, exactly mirroring how
/// <see cref="SharedKernelDbContext.CurrentActor"/> is attached. This is what makes
/// <c>EfCorePersistenceBuilder.WithDbContextPooling()</c> safely combinable with
/// <c>WithMultiTenancy()</c>: EF Core's pooled-context activator never has a genuinely Scoped
/// service to resolve for this class's own constructor. Every path that hands a
/// <see cref="TenantedDbContext"/> instance to calling code — the scoped <c>TContext</c>
/// registration, the decorated <c>IDbContextFactory&lt;TContext&gt;</c>, and
/// <see cref="ReadReplica.ReadReplicaContextAccessor{TContext}"/> — calls <see cref="RefreshTenant"/>
/// immediately after obtaining the instance, whether it was freshly constructed or reused from the
/// pool. <see cref="Dispose"/>/<see cref="DisposeAsync"/> additionally reset
/// <see cref="CurrentTenant"/> back to <see cref="MultiTenancy.NullCurrentTenantContext.Instance"/>
/// BEFORE calling the base implementation — EF Core 10 has no public/overridable
/// "returned to pool" hook (<c>IResettableService</c>/<c>IDbContextPoolable</c> are internal,
/// explicit-interface-implemented on <see cref="DbContext"/> itself, confirmed by reflection), but
/// <c>Dispose()</c>/<c>DisposeAsync()</c> ARE public virtual and ARE the exact method the pool's
/// return-to-pool interception hangs off — so resetting here is genuinely fail-closed defense in
/// depth against a caller that keeps using a reference after disposing it (a bug either way, since
/// <see cref="DbContext"/> is never safe for concurrent/post-dispose use, but one that now reads and
/// writes nothing instead of silently serving a leaked instance's stale — or a reused pool slot's
/// NEXT — tenant's data).
/// </para>
/// <para>
/// <strong>Filter implementation:</strong> the global filter lambda is built using expression trees
/// (<c>Expression.Parameter</c>, <c>Expression.Property</c>, <c>Expression.Constant</c>,
/// <c>Expression.Equal</c>, <c>Expression.Lambda</c>) via the non-generic
/// <c>modelBuilder.Entity(clrType).HasQueryFilter(key, lambda)</c> overload. The filter binds through
/// <c>Expression.Constant(this, GetType())</c> → <see cref="CurrentTenant"/> →
/// <c>TenantId</c> — a captured "this DbContext instance" constant, rebound by EF Core's
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
/// must enter an <see cref="ICrossTenantScope"/> explicitly (<c>ICrossTenantScope.Enter()</c>), then
/// call <c>.IgnoreQueryFilters([PersistenceFilterNames.Tenant])</c> — never a bare
/// <c>.IgnoreQueryFilters()</c>, which would also drop the soft-delete filter — or use
/// <c>TenantedRepository&lt;T,TId&gt;.GetByIdForTenantAsync</c>, which now enforces the same
/// active-scope requirement.
/// </para>
/// </remarks>
public abstract class TenantedDbContext : SharedKernelDbContext
{
    // Model-build-time-only reflection lookup for the public CurrentTenant property declared on
    // this class — never invoked in a query hot path. Public, so the string-name overload of
    // Expression.Property (Type.GetProperty(string)) already finds it without BindingFlags.
    private static readonly System.Reflection.PropertyInfo CurrentTenantPropertyInfo =
        typeof(TenantedDbContext).GetProperty(nameof(CurrentTenant))!;

    private ICurrentTenantContext _currentTenant = NullCurrentTenantContext.Instance;

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
    /// <remarks>
    /// Deliberately takes NO <see cref="ICurrentTenantContext"/> constructor parameter —
    /// see the class remarks above ("real pooling fix, not a guard"). <see cref="CurrentTenant"/>
    /// starts fail-closed; the caller obtaining this instance (never application code directly —
    /// always <c>EfCorePersistenceBuilder</c>'s registrations) is responsible for calling
    /// <see cref="RefreshTenant"/> before returning it to application code.
    /// </remarks>
    protected TenantedDbContext(DbContextOptions options, PersistenceContextDependencies dependencies)
        : base(options, dependencies)
    {
    }

    /// <summary>
    /// Gets the <see cref="ICurrentTenantContext"/> the tenant global query filter and
    /// <c>TenantWriteGuardInterceptor</c> resolve tenant identity from.
    /// </summary>
    /// <remarks>
    /// Fail-closed (<see cref="MultiTenancy.NullCurrentTenantContext.Instance"/>) until
    /// <see cref="RefreshTenant"/> is called — which every supported way of obtaining a
    /// <see cref="TenantedDbContext"/> instance does exactly once, immediately, before handing it to
    /// application code. Reset back to fail-closed on <see cref="Dispose"/>/
    /// <see cref="DisposeAsync"/>.
    /// </remarks>
    public ICurrentTenantContext CurrentTenant => _currentTenant;

    /// <summary>Replaces <see cref="CurrentTenant"/> with <paramref name="tenantContext"/>.</summary>
    /// <param name="tenantContext">The current scope's real <see cref="ICurrentTenantContext"/>.</param>
    /// <remarks>
    /// <see langword="internal"/> — called only by
    /// <c>EfCorePersistenceBuilder</c>'s own registrations (the scoped <c>TContext</c> factory
    /// delegate, the decorated <c>IDbContextFactory&lt;TContext&gt;</c>) and
    /// <see cref="ReadReplica.ReadReplicaContextAccessor{TContext}"/>, within this same assembly. Not
    /// a public extensibility seam.
    /// </remarks>
    internal void RefreshTenant(ICurrentTenantContext tenantContext) => _currentTenant = tenantContext;

    /// <inheritdoc />
    /// <remarks>
    /// Resets <see cref="CurrentTenant"/> to the fail-closed
    /// <see cref="MultiTenancy.NullCurrentTenantContext.Instance"/> BEFORE calling
    /// <see cref="DbContext.Dispose()"/> — see the class remarks for why this, not
    /// <c>IResettableService</c>, is the real "returned to pool" hook available in EF Core 10.
    /// </remarks>
    public override void Dispose()
    {
        _currentTenant = NullCurrentTenantContext.Instance;
        base.Dispose();
    }

    /// <inheritdoc />
    /// <remarks>See <see cref="Dispose"/>.</remarks>
    public override async ValueTask DisposeAsync()
    {
        _currentTenant = NullCurrentTenantContext.Instance;
        await base.DisposeAsync();
    }

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
    // plays no part in it. TenantWriteGuardInterceptor already rejects any entry whose IN-MEMORY
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
    // TenantWriteGuardInterceptor already required to equal the caller's current tenant. The WHERE
    // clause therefore becomes "Id = <target> AND TenantId = <caller's own tenant>": a victim row
    // belonging to a DIFFERENT tenant matches zero rows, and EF Core raises
    // DbUpdateConcurrencyException instead of silently succeeding — translated by
    // ConcurrencyInterceptor.TryTranslate into the same tenant-isolation Forbidden error
    // TenantWriteGuardInterceptor raises proactively.
    private static void ApplyTenantConcurrencyToken(ModelBuilder modelBuilder, Type clrType) =>
        modelBuilder.Entity(clrType).Property(nameof(IHasTenant.TenantId)).IsConcurrencyToken();

    // Builds a lambda: e => this.CurrentTenant.TenantId.HasValue && e.TenantId == this.CurrentTenant.TenantId
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

        // this.CurrentTenant
        var currentTenantAccess = Expression.Property(thisConst, CurrentTenantPropertyInfo);

        // this.CurrentTenant.TenantId (Guid?)
        var tenantIdAccess = Expression.Property(currentTenantAccess, nameof(ICurrentTenantContext.TenantId));

        // this.CurrentTenant.TenantId.HasValue
        var hasValue = Expression.Property(tenantIdAccess, nameof(Nullable<Guid>.HasValue));

        // e.TenantId == this.CurrentTenant.TenantId (Guid promoted to Guid? for the comparison)
        var equalExpr = Expression.Equal(Expression.Convert(tenantIdProperty, typeof(Guid?)), tenantIdAccess);

        // this.CurrentTenant.TenantId.HasValue && e.TenantId == this.CurrentTenant.TenantId
        var guarded = Expression.AndAlso(hasValue, equalExpr);

        // e =>...
        var lambda = Expression.Lambda(guarded, param);

        // Apply via non-generic overload, named "Tenant" — no reflection on entity type needed.
        modelBuilder.Entity(clrType).HasQueryFilter(PersistenceFilterNames.Tenant, lambda);
    }
}
