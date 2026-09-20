using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence.EfCore.Context;

/// <summary>
/// Scoped <see cref="IDbContextFactory{TContext}"/> decorator that attaches the CURRENT DI scope's
/// <see cref="ICurrentActorContext"/> and (for a <see cref="MultiTenancy.TenantedDbContext"/>
/// subclass) <see cref="ICurrentTenantContext"/> to every instance it hands out — whether the inner
/// factory constructs a fresh instance or leases a reused one from a pool.
/// </summary>
/// <typeparam name="TContext">The concrete <see cref="SharedKernelDbContext"/> subclass.</typeparam>
/// <remarks>
/// <para>
/// This is the ONE place every path that
/// can produce a <typeparamref name="TContext"/> instance funnels through:
/// <c>EfCorePersistenceBuilder&lt;TContext&gt;.Build()</c> registers this type — SCOPED — as the
/// public, unkeyed <see cref="IDbContextFactory{TContext}"/>, wrapping the real (pooled or
/// non-pooled) factory it registers under a private key. The scoped <c>TContext</c> registration
/// (for consumer code injecting <typeparamref name="TContext"/> directly) resolves this decorated
/// factory and calls <see cref="CreateDbContext"/> on it, so direct injection and explicit
/// <see cref="IDbContextFactory{TContext}"/> injection (the shape a background worker/hosted service
/// uses) both get identical, correct-per-scope attachment.
/// </para>
/// <para>
/// Because this decorator itself is registered SCOPED, a background worker that wants a specific
/// tenant/actor identity for its own unit of work creates its own <see cref="IServiceScope"/>
/// (<c>IServiceProvider.CreateScope()</c>/<c>CreateAsyncScope()</c>) and registers (or lets DI
/// resolve) the identity it wants visible to that scope's <see cref="ICurrentActorContext"/>/
/// <see cref="ICurrentTenantContext"/> BEFORE resolving <see cref="IDbContextFactory{TContext}"/>
/// from it — every context created via that scope's factory then carries that identity. A scope with
/// no explicit tenant registered gets the builder's own fail-closed default
/// (<see cref="MultiTenancy.NullCurrentTenantContext"/>), so an "unattached" background scope reads
/// and writes nothing rather than silently reusing whichever tenant a pooled instance happened to
/// carry before.
/// </para>
/// </remarks>
internal sealed class TenantAwareDbContextFactory<TContext> : IDbContextFactory<TContext>
    where TContext : SharedKernelDbContext
{
    private readonly IDbContextFactory<TContext> _inner;
    private readonly ICurrentActorContext _actorContext;
    private readonly ICurrentTenantContext _tenantContext;

    /// <summary>Initialises a new <see cref="TenantAwareDbContextFactory{TContext}"/>.</summary>
    /// <param name="inner">The real (pooled or non-pooled) factory this decorator wraps.</param>
    /// <param name="actorContext">The current scope's <see cref="ICurrentActorContext"/>.</param>
    /// <param name="tenantContext">The current scope's <see cref="ICurrentTenantContext"/>.</param>
    public TenantAwareDbContextFactory(
        IDbContextFactory<TContext> inner,
        ICurrentActorContext actorContext,
        ICurrentTenantContext tenantContext)
    {
        _inner = inner;
        _actorContext = actorContext;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public TContext CreateDbContext()
    {
        var context = _inner.CreateDbContext();
        Attach(context);
        return context;
    }

    /// <inheritdoc />
    public async Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        // Deliberately calls the INNER factory's own CreateDbContextAsync (never this type's
        // CreateDbContext()) — PooledDbContextFactory overrides it with genuine async pool-lease
        // behavior; falling back to the sync path here would silently lose that.
        var context = await _inner.CreateDbContextAsync(cancellationToken);
        Attach(context);
        return context;
    }

    private void Attach(TContext context)
    {
        context.RefreshActor(_actorContext);

        if (context is TenantedDbContext tenanted)
            tenanted.RefreshTenant(_tenantContext);
    }
}
