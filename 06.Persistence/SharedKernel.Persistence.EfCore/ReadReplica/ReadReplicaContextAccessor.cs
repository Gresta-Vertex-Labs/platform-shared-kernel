using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.EfCore.Context;

namespace SharedKernel.Persistence.EfCore.ReadReplica;

/// <summary>
/// Default <see cref="IReadReplicaContextAccessor{TContext}"/> implementation, registered by
/// <c>EfCorePersistenceBuilder{TContext}.WithReadReplica(...)</c> (WO-053/P-338).
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
/// instances (and their live <c>CurrentUserContext</c>, WO-051/P-322) the primary context for this
/// scope already resolved — audit-field consistency between primary and replica reads is automatic,
/// with zero extra plumbing.
/// </para>
/// </remarks>
internal sealed class ReadReplicaContextAccessor<TContext> : IReadReplicaContextAccessor<SharedKernelDbContext>
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

        return _replicaContext ??= ActivatorUtilities.CreateInstance<TContext>(_serviceProvider, _replicaOptions);
    }
}
