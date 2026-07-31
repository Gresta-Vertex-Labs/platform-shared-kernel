using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// Abstract EF Core DbContext base for multi-tenant services.
/// Installs a runtime-captured global query filter on all <see cref="IHasTenant"/> entities
/// so that every query is automatically scoped to the current tenant.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Filter semantics:</strong> the global filter is <c>e.TenantId == tenantProvider.TenantId</c>.
/// The <em>value</em> of <see cref="ITenantProvider.TenantId"/> is resolved at query-execution
/// time, not at startup. This means rotating the current tenant (e.g., across HTTP requests in
/// a scoped context) works correctly without rebuilding the context.
/// </para>
/// <para>
/// <strong>Zero-row sentinel:</strong> when <see cref="NoOpTenantProvider"/> is active (or any
/// provider returning <see cref="Guid.Empty"/>), the filter becomes
/// <c>e.TenantId == Guid.Empty</c> which returns zero rows. No production entity should carry
/// <c>TenantId == Guid.Empty</c>. This is intentional — it prevents cross-tenant data leaks when
/// no real tenant provider is registered.
/// </para>
/// <para>
/// <strong>Filter implementation (CORRECTED, WO-051/P-322):</strong> the global filter lambda is
/// built using expression trees (<c>Expression.Parameter</c>, <c>Expression.Property</c>,
/// <c>Expression.Constant</c>, <c>Expression.Equal</c>, <c>Expression.Lambda</c>) via the
/// non-generic <c>modelBuilder.Entity(clrType).HasQueryFilter</c> overload. The filter binds
/// through <c>Expression.Constant(this, GetType())</c> → <see cref="TenantProvider"/> →
/// <c>TenantId</c> — a captured "this DbContext instance" constant, NOT a specific captured
/// <see cref="ITenantProvider"/> object. EF Core's query-filter compilation specially rebinds this
/// exact shape to whichever instance is EXECUTING the query, not the instance whose
/// <see cref="OnModelCreating"/> built the (process-wide-cached) model — the same idiom
/// Microsoft's own multi-tenancy sample uses. This closes a confirmed pre-existing defect
/// (independent of pooling): the previous design baked
/// <c>Expression.Constant(specificProviderObject, typeof(ITenantProvider))</c> into the compiled
/// filter, permanently freezing every subsequent query — for the process's entire lifetime — to
/// whichever <see cref="ITenantProvider"/> instance constructed the very first
/// <see cref="TenantedDbContext"/> of this concrete type, because EF Core's default model cache is
/// keyed only by context type and is shared process-wide. Because <see cref="TenantProvider"/> is
/// read off <c>this</c> live on every query, it is also what makes
/// <see cref="RefreshRequestContext"/> effective under <c>.WithDbContextPooling()</c>. The
/// <c>PropertyInfo</c> lookup for <see cref="TenantProvider"/> (a protected property declared on
/// this very class) is a one-time, model-build-time-only reflection call — not a per-query hot
/// path — mirroring the platform's existing documented startup-time reflection exceptions
/// (<c>ValueObjectOwnershipBuilder</c>, <c>EncryptedEntityBatchProcessorRegistry</c>). No
/// <c>GetMethod</c>/<c>MakeGenericMethod</c>/<c>Invoke</c> dispatch pattern is used anywhere in
/// this class.
/// </para>
/// <para>
/// Multi-tenant services must extend this class instead of <see cref="SharedKernelDbContext"/>.
/// Single-tenant services extend <see cref="SharedKernelDbContext"/> directly.
/// </para>
/// <para>
/// <strong>Cross-tenant access:</strong> admin or migration paths that need to bypass the filter
/// should call <c>.IgnoreQueryFilters()</c> on the relevant queryable, or use
/// <c>TenantedRepository&lt;T,TId&gt;.GetByIdForTenantAsync</c>.
/// </para>
/// </remarks>
public abstract class TenantedDbContext : SharedKernelDbContext
{
    // WO-051/P-322: one-time, model-build-time-only reflection lookup for the TenantProvider
    // property declared below — never invoked in a query hot path. See this class's own remarks
    // for why a PropertyInfo-based Expression.Property is required here (TenantProvider is
    // protected, so the string-name overload of Expression.Property, which only finds PUBLIC
    // members via Type.GetProperty(string), cannot locate it).
    private static readonly PropertyInfo TenantProviderPropertyInfo =
        typeof(TenantedDbContext).GetProperty(nameof(TenantProvider), BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Provides access to the current tenant provider for subclass model configuration.</summary>
    /// <remarks>
    /// Settable only via <see cref="RefreshRequestContext"/> (WO-051/P-322) — see that method and
    /// this class's own remarks for the pooling-safety and pre-existing-staleness-defect rationale.
    /// </remarks>
    protected ITenantProvider TenantProvider { get; private set; }

    /// <summary>
    /// Initialises a new <see cref="TenantedDbContext"/>.
    /// </summary>
    /// <param name="options">EF Core context options supplied by the DI container.</param>
    /// <param name="auditInterceptor">Scoped interceptor that populates audit fields.</param>
    /// <param name="softDeleteInterceptor">Scoped interceptor that converts deletes to soft-deletes.</param>
    /// <param name="concurrencyInterceptor">Interceptor that wraps concurrency exceptions.</param>
    /// <param name="tenantProvider">
    /// Service that resolves the current tenant identifier at query-execution time.
    /// </param>
    protected TenantedDbContext(
        DbContextOptions options,
        AuditInterceptor auditInterceptor,
        SoftDeleteInterceptor softDeleteInterceptor,
        ConcurrencyInterceptor concurrencyInterceptor,
        ITenantProvider tenantProvider)
        : base(options, auditInterceptor, softDeleteInterceptor, concurrencyInterceptor)
    {
        TenantProvider = tenantProvider;
    }

    /// <summary>
    /// Replaces both <see cref="SharedKernelDbContext.CurrentUserContext"/> (via the base class) and
    /// <see cref="TenantProvider"/> with the supplied instances.
    /// </summary>
    /// <param name="userContext">The current scope's real <see cref="IUserContext"/>.</param>
    /// <param name="tenantProvider">The current scope's real <see cref="ITenantProvider"/>.</param>
    /// <remarks>
    /// Called once per lease by the factory delegate <c>EfCorePersistenceBuilder.WithDbContextPooling()</c>
    /// registers, for <see cref="TenantedDbContext"/>-derived contexts. Non-pooled consumers never
    /// need to call this — the constructor-set value is already correct for a non-pooled instance's
    /// lifetime (a fresh instance is constructed per DI scope).
    /// </remarks>
    public void RefreshRequestContext(IUserContext userContext, ITenantProvider tenantProvider)
    {
        RefreshUserContext(userContext);
        TenantProvider = tenantProvider;
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
    /// Installs the expression-tree tenant query filter on all <see cref="IHasTenant"/> entity
    /// types currently registered in the model. Call this after all entity configurations are applied.
    /// </summary>
    /// <param name="modelBuilder">The builder used to construct the model for this context.</param>
    /// <remarks>
    /// Subclasses that bypass the assembly-scan path in <c>OnModelCreating</c> should call this
    /// method explicitly after applying their entity configurations to ensure tenant isolation is
    /// preserved.
    /// </remarks>
    protected void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(IHasTenant).IsAssignableFrom(entityType.ClrType))
                continue;

            ApplyTenantFilterExpression(modelBuilder, entityType.ClrType);
        }
    }

    // Builds a lambda: e => e.TenantId == this.TenantProvider.TenantId
    // using expression trees, where "this" is a captured DbContext-instance constant that EF Core
    // rebinds to whichever instance is executing the query (WO-051/P-322 — see this class's own
    // remarks for the full rationale and the pre-existing defect this corrects).
    private void ApplyTenantFilterExpression(ModelBuilder modelBuilder, Type clrType)
    {
        // Parameter: e
        var param = Expression.Parameter(clrType, "e");

        // e.TenantId
        var tenantIdProperty = Expression.Property(param, nameof(IHasTenant.TenantId));

        // this  — a captured "this DbContext instance" constant. EF Core's query-filter compilation
        // recognizes a ConstantExpression whose Value is the DbContext instance the model was built
        // from and rebinds it, per query execution, to the CURRENT executing instance — never a
        // frozen reference to whichever instance first built the (process-wide-cached) model.
        var thisConst = Expression.Constant(this, GetType());

        // this.TenantProvider
        var tenantProviderAccess = Expression.Property(thisConst, TenantProviderPropertyInfo);

        // this.TenantProvider.TenantId
        var tenantIdAccess = Expression.Property(tenantProviderAccess, nameof(ITenantProvider.TenantId));

        // e.TenantId == this.TenantProvider.TenantId
        var equalExpr = Expression.Equal(tenantIdProperty, tenantIdAccess);

        // e => e.TenantId == this.TenantProvider.TenantId
        var lambda = Expression.Lambda(equalExpr, param);

        // Apply via non-generic overload — no reflection on entity type needed
        modelBuilder.Entity(clrType).HasQueryFilter(lambda);
    }
}
