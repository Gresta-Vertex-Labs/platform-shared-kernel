using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence.EfCore.Context;

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
/// <strong>Every entity type is tenant data.</strong> The filter and the write guard apply to every
/// <see cref="IHasTenant"/> entity type, children of aggregates included, and the model build fails for a
/// non-owned entity type that is neither <see cref="IHasTenant"/> nor declared tenant-shared
/// (<see cref="TenantSharedAttribute"/>, <c>IsTenantShared()</c>). An added entity whose <c>TenantId</c> is unset gets
/// the caller's tenant. A TPH-derived type shares its root's filter.
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
    public TenantId? CurrentTenantId => RequestContext.TenantId;

    /// <summary>
    /// Adds the tenant-isolation convention after the base conventions: the named tenant filter and the
    /// <c>TenantId</c> concurrency token on every <see cref="IHasTenant"/> root type, and a model error for any
    /// other non-owned entity type not declared tenant-shared (<see cref="TenantSharedAttribute"/> or
    /// <c>IsTenantShared()</c>).
    /// </summary>
    /// <param name="configurationBuilder">The convention builder.</param>
    /// <remarks>
    /// The convention runs when the model is finalized, after every <c>OnModelCreating</c> configuration, so an
    /// entity type configured after <c>base.OnModelCreating</c> is covered too.
    /// </remarks>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Conventions.Add(_ => new TenantIsolationConvention(this));
    }
}
