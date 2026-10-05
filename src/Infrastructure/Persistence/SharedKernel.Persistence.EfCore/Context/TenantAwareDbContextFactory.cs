using Microsoft.EntityFrameworkCore;
using SharedKernel.Execution.Context;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;

namespace SharedKernel.Persistence.EfCore.Context;

/// <summary>
/// The public, <b>scoped</b> <see cref="IDbContextFactory{TContext}"/>: attaches the resolving scope's caller
/// (<see cref="IRequestContext"/>) and domain-event dispatcher to every context it hands out, fresh or pooled.
/// </summary>
/// <typeparam name="TContext">The concrete context.</typeparam>
/// <remarks>
/// Every way of getting a <typeparamref name="TContext"/> from the container goes through this factory (direct
/// injection of <typeparamref name="TContext"/> included). It is scoped on purpose: the identity belongs to the
/// scope. A singleton (a hosted service) creates a scope and resolves the factory from it, or injects the
/// singleton <see cref="ICallerDbContextFactory{TContext}"/> and passes the caller explicitly.
/// </remarks>
internal sealed class TenantAwareDbContextFactory<TContext>(
    IDbContextFactory<TContext> inner,
    IRequestContext requestContext,
    IDomainEventDispatcher? domainEventDispatcher,
    ICrossTenantScope crossTenantScope) : IDbContextFactory<TContext>
    where TContext : SharedKernelDbContext
{
    /// <inheritdoc />
    public TContext CreateDbContext()
    {
        var context = inner.CreateDbContext();
        context.AttachLease(requestContext, domainEventDispatcher, crossTenantScope);
        return context;
    }

    /// <inheritdoc />
    public async Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        // The inner factory's own async method: the pooled factory leases asynchronously.
        var context = await inner.CreateDbContextAsync(cancellationToken);
        context.AttachLease(requestContext, domainEventDispatcher, crossTenantScope);
        return context;
    }
}

/// <summary>
/// A <b>singleton-safe</b> context factory for code without a request scope — hosted services, scheduled jobs,
/// workflow activities — that takes the caller explicitly instead of capturing a scoped identity.
/// </summary>
/// <typeparam name="TContext">The concrete context.</typeparam>
/// <remarks>
/// <para>
/// The standard <see cref="IDbContextFactory{TContext}"/> is scoped and attaches the identity of the scope it
/// was resolved from; injecting it into a singleton would capture one scope's caller forever (or fail scope
/// validation). This factory has no identity of its own:
/// </para>
/// <code>
/// await using var db = await factory.CreateDbContextAsync(new SystemRequestContext([], "nightly-billing"), ct);
/// </code>
/// <para>The caller disposes the context. A dispatcher passed here receives the context's domain events.</para>
/// </remarks>
#pragma warning disable RS0026 // The dispatcher overload takes it as a required parameter, so the argument count selects the overload.
public interface ICallerDbContextFactory<TContext>
    where TContext : SharedKernelDbContext
{
    /// <summary>
    /// Creates a context acting as <paramref name="caller"/>, without a domain-event dispatcher (pending events are
    /// discarded with a warning; pass one to the other overload when the work raises events).
    /// </summary>
    /// <param name="caller">The identity (and tenant) the context attributes and filters by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new (or pooled) context the caller must dispose.</returns>
    Task<TContext> CreateDbContextAsync(IRequestContext caller, CancellationToken cancellationToken = default);

    /// <summary>Creates a context acting as <paramref name="caller"/>, with an explicit domain-event dispatcher.</summary>
    /// <param name="caller">The identity (and tenant) the context attributes and filters by.</param>
    /// <param name="domainEventDispatcher">Receives domain events before each save; <see langword="null"/> discards them with a warning.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new (or pooled) context the caller must dispose.</returns>
    Task<TContext> CreateDbContextAsync(
        IRequestContext caller,
        IDomainEventDispatcher? domainEventDispatcher,
        CancellationToken cancellationToken = default);
}
#pragma warning restore RS0026

/// <summary>Default <see cref="ICallerDbContextFactory{TContext}"/> over the registration's inner factory.</summary>
internal sealed class CallerDbContextFactory<TContext>(IDbContextFactory<TContext> inner) : ICallerDbContextFactory<TContext>
    where TContext : SharedKernelDbContext
{
    /// <inheritdoc />
    public Task<TContext> CreateDbContextAsync(IRequestContext caller, CancellationToken cancellationToken = default) =>
        CreateDbContextAsync(caller, domainEventDispatcher: null, cancellationToken);

    /// <inheritdoc />
    public async Task<TContext> CreateDbContextAsync(
        IRequestContext caller,
        IDomainEventDispatcher? domainEventDispatcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var context = await inner.CreateDbContextAsync(cancellationToken);
        context.AttachLease(caller, domainEventDispatcher, crossTenantScope: null);
        return context;
    }
}
