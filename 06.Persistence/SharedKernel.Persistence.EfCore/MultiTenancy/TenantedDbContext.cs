using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// Abstract EF Core DbContext base for multi-tenant services.
/// Installs a runtime-captured global query filter on all <see cref="IHasTenant"/> entities
/// so that every query is automatically scoped to the current tenant.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Filter semantics:</strong> the global filter is <c>e.TenantId == currentTenantService.TenantId</c>.
/// The <see cref="ICurrentTenantService"/> reference is captured at construction time; the
/// <em>value</em> of <see cref="ICurrentTenantService.TenantId"/> is resolved at query-execution
/// time, not at startup. This means rotating the current tenant (e.g., across HTTP requests in
/// a scoped context) works correctly without rebuilding the context.
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
    /// <summary>Provides access to the current tenant service for subclass model configuration.</summary>
    protected ICurrentTenantService CurrentTenantService { get; }

    /// <summary>
    /// Initialises a new <see cref="TenantedDbContext"/>.
    /// </summary>
    /// <param name="options">EF Core context options supplied by the DI container.</param>
    /// <param name="auditInterceptor">Scoped interceptor that populates audit fields.</param>
    /// <param name="softDeleteInterceptor">Scoped interceptor that converts deletes to soft-deletes.</param>
    /// <param name="concurrencyInterceptor">Interceptor that wraps concurrency exceptions.</param>
    /// <param name="currentTenantService">
    /// Service that resolves the current tenant at query-execution time.
    /// </param>
    protected TenantedDbContext(
        DbContextOptions options,
        AuditInterceptor auditInterceptor,
        SoftDeleteInterceptor softDeleteInterceptor,
        ConcurrencyInterceptor concurrencyInterceptor,
        ICurrentTenantService currentTenantService)
        : base(options, auditInterceptor, softDeleteInterceptor, concurrencyInterceptor)
    {
        CurrentTenantService = currentTenantService;
    }

    /// <summary>
    /// Applies the tenant global query filter in addition to the base configurations.
    /// </summary>
    /// <param name="modelBuilder">The builder used to construct the model for this context.</param>
    /// <remarks>
    /// Downstream contexts that override this method must call
    /// <c>base.OnModelCreating(modelBuilder)</c> first to ensure all conventions are applied.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(IHasTenant).IsAssignableFrom(entityType.ClrType))
                continue;

            // Build a runtime-captured lambda: e => e.TenantId == CurrentTenantService.TenantId
            // We use the generic helper to avoid reflection on the entity property in the hot path.
            var method = typeof(TenantedDbContext)
                .GetMethod(nameof(ApplyTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(entityType.ClrType);

            method.Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IHasTenant
    {
        // The lambda captures CurrentTenantService — tenant ID is resolved at query time.
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == CurrentTenantService.TenantId);
    }
}
