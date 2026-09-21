using Microsoft.EntityFrameworkCore;
using SharedKernel.Application.Context;

namespace SharedKernel.Persistence.EfCore.Context;

/// <summary>
/// Scoped <see cref="IDbContextFactory{TContext}"/> decorator that attaches the CURRENT DI scope's
/// <see cref="IRequestContext"/> — caller identity and tenant — to every instance it hands out,
/// whether the inner factory constructs a fresh instance or leases a reused one from a pool.
/// </summary>
/// <typeparam name="TContext">The concrete <see cref="SharedKernelDbContext"/> subclass.</typeparam>
/// <remarks>
/// <para>
/// This is the ONE place every path that can produce a <typeparamref name="TContext"/> instance funnels
/// through: <c>EfCorePersistenceBuilder&lt;TContext&gt;.Build()</c> registers this type — SCOPED — as
/// the public, unkeyed <see cref="IDbContextFactory{TContext}"/>, wrapping the real (pooled or
/// non-pooled) factory it registers under a private key. Direct <typeparamref name="TContext"/>
/// injection resolves this factory too, so both shapes get identical per-scope attachment.
/// </para>
/// <para>
/// A background worker that wants a specific identity creates its own scope, makes that scope's
/// <see cref="IRequestContext"/> report it (for example a <see cref="SystemRequestContext"/>), then
/// resolves <see cref="IDbContextFactory{TContext}"/> from it. A scope with nothing registered gets the
/// builder's fail-closed <see cref="AnonymousRequestContext"/>: no tenant, so it reads and writes no
/// tenant-scoped row.
/// </para>
/// </remarks>
internal sealed class TenantAwareDbContextFactory<TContext> : IDbContextFactory<TContext>
    where TContext : SharedKernelDbContext
{
    private readonly IDbContextFactory<TContext> _inner;
    private readonly IRequestContext _requestContext;

    /// <summary>Initialises a new <see cref="TenantAwareDbContextFactory{TContext}"/>.</summary>
    /// <param name="inner">The real (pooled or non-pooled) factory this decorator wraps.</param>
    /// <param name="requestContext">The current scope's <see cref="IRequestContext"/>.</param>
    public TenantAwareDbContextFactory(IDbContextFactory<TContext> inner, IRequestContext requestContext)
    {
        _inner = inner;
        _requestContext = requestContext;
    }

    /// <inheritdoc />
    public TContext CreateDbContext()
    {
        var context = _inner.CreateDbContext();
        context.RefreshRequestContext(_requestContext);
        return context;
    }

    /// <inheritdoc />
    public async Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        // Deliberately the INNER factory's own CreateDbContextAsync — PooledDbContextFactory overrides
        // it with a genuine async pool lease.
        var context = await _inner.CreateDbContextAsync(cancellationToken);
        context.RefreshRequestContext(_requestContext);
        return context;
    }
}
