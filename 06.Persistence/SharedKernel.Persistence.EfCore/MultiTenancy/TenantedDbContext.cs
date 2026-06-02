using System.Linq.Expressions;
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
/// The <see cref="ITenantProvider"/> reference is captured at construction time; the
/// <em>value</em> of <see cref="ITenantProvider.TenantId"/> is resolved at query-execution
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
/// <strong>Filter implementation:</strong> the global filter lambda is built using expression trees
/// (<c>Expression.Parameter</c>, <c>Expression.Property</c>, <c>Expression.Equal</c>,
/// <c>Expression.Lambda</c>) via the non-generic <c>modelBuilder.Entity(clrType).HasQueryFilter</c>
/// overload. No <c>GetMethod</c>, <c>MakeGenericMethod</c>, or <c>Invoke</c> calls are used —
/// the implementation is fully AOT-safe.
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
    /// <summary>Provides access to the current tenant provider for subclass model configuration.</summary>
    protected ITenantProvider TenantProvider { get; }

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

    // Builds a runtime-captured lambda: e => e.TenantId == TenantProvider.TenantId
    // using expression trees — no GetMethod/MakeGenericMethod/Invoke.
    private void ApplyTenantFilterExpression(ModelBuilder modelBuilder, Type clrType)
    {
        // Parameter: e
        var param = Expression.Parameter(clrType, "e");

        // e.TenantId
        var tenantIdProperty = Expression.Property(param, nameof(IHasTenant.TenantId));

        // () => TenantProvider.TenantId  — captured at model-build time; value read at query time
        // We need a live closure over TenantProvider so that the filter sees the per-request value.
        // Capture via a local variable that Expression.Constant holds as an object.
        var provider = TenantProvider;
        var providerConst = Expression.Constant(provider, typeof(ITenantProvider));
        var tenantIdAccess = Expression.Property(providerConst, nameof(ITenantProvider.TenantId));

        // e.TenantId == provider.TenantId
        var equalExpr = Expression.Equal(tenantIdProperty, tenantIdAccess);

        // e => e.TenantId == provider.TenantId
        var lambda = Expression.Lambda(equalExpr, param);

        // Apply via non-generic overload — no reflection on entity type needed
        modelBuilder.Entity(clrType).HasQueryFilter(lambda);
    }
}
