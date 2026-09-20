using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence.EfCore.ReadReplica;

/// <summary>
/// Default <see cref="IReadReplicaContextAccessor{TContext}"/> implementation, registered by
/// <c>EfCorePersistenceBuilder{TContext}.WithReadReplica(...)</c>.
/// </summary>
/// <typeparam name="TContext">
/// The consuming service's concrete <see cref="SharedKernelDbContext"/> subclass. Used internally
/// to lazily construct the replica instance; this class always implements
/// <see cref="IReadReplicaContextAccessor{TContext}"/> closed at <see cref="SharedKernelDbContext"/>
/// (not <typeparamref name="TContext"/>) — see that interface's own remarks for why.
/// </typeparam>
/// <remarks>
/// <para>
/// Registered scoped — one instance per DI scope, so <see cref="_replicaContext"/> naturally
/// caches the lazily-constructed replica for that scope's lifetime without any additional
/// bookkeeping.
/// </para>
/// <para>
/// The replica <typeparamref name="TContext"/> instance is constructed via
/// <see cref="ActivatorUtilities.CreateInstance{T}(IServiceProvider, object[])"/> against the
/// CURRENT scope's <see cref="IServiceProvider"/> — deliberately reusing the same scope-ambient,
/// DI-resolved <c>AuditInterceptor</c>/<c>SoftDeleteInterceptor</c>/<c>ConcurrencyInterceptor</c>
/// instances (and their live <c>CurrentActor</c>) the primary context for this
/// scope already resolved — audit-field consistency between primary and replica reads is automatic,
/// with zero extra plumbing.
/// </para>
/// <para>
/// <strong>Disposal:</strong> the lazily-constructed replica context is a real
/// <see cref="DbContext"/> holding a real database connection — it MUST be disposed, exactly like
/// the primary context DI already disposes at scope end. This class implements
/// <see cref="IAsyncDisposable"/> for that reason: because it is registered scoped (via a factory
/// delegate, not a bare type registration), the DI container still tracks and disposes ANY instance
/// it creates that implements <see cref="IAsyncDisposable"/>/<see cref="IDisposable"/>, so no
/// additional wiring is needed beyond implementing the interface here.
/// </para>
/// </remarks>
internal sealed class ReadReplicaContextAccessor<TContext> : IReadReplicaContextAccessor<SharedKernelDbContext>, IAsyncDisposable
    where TContext : SharedKernelDbContext
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DbContextOptions<TContext> _replicaOptions;
    private TContext? _replicaContext;

    /// <summary>Initialises a new <see cref="ReadReplicaContextAccessor{TContext}"/>.</summary>
    /// <param name="serviceProvider">The current DI scope's service provider.</param>
    /// <param name="replicaOptions">The replica connection's <see cref="DbContextOptions{TContext}"/>.</param>
    public ReadReplicaContextAccessor(IServiceProvider serviceProvider, DbContextOptions<TContext> replicaOptions)
    {
        _serviceProvider = serviceProvider;
        _replicaOptions = replicaOptions;
    }

    /// <inheritdoc />
    public SharedKernelDbContext GetEffectiveContext(SharedKernelDbContext primaryContext)
    {
        if (primaryContext.Database.CurrentTransaction is not null)
            return primaryContext;

        if (_replicaContext is not null)
            return _replicaContext;

        var replica = ActivatorUtilities.CreateInstance<TContext>(_serviceProvider, _replicaOptions);

        // TenantedDbContext's constructor no longer takes ICurrentTenantContext (see its
        // own remarks) — ActivatorUtilities.CreateInstance can no longer attach it as a constructor
        // argument, so it must be attached explicitly here, from the SAME scope-ambient
        // ICurrentTenantContext the primary context's own TenantAwareDbContextFactory already used.
        if (replica is TenantedDbContext tenanted)
            tenanted.RefreshTenant(_serviceProvider.GetRequiredService<ICurrentTenantContext>());

        return _replicaContext = replica;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_replicaContext is not null)
            await _replicaContext.DisposeAsync();
    }
}
