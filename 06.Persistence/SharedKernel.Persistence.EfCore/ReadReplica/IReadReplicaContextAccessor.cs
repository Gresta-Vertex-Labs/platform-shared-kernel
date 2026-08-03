using SharedKernel.Persistence.EfCore.Context;

namespace SharedKernel.Persistence.EfCore.ReadReplica;

/// <summary>
/// Resolves the <see cref="SharedKernelDbContext"/> instance an
/// <c>EfReadRepository{TAggregate,TId}</c> read should execute against — either the primary
/// context passed in, or a lazily-constructed read-replica context (WO-053/P-338).
/// </summary>
/// <typeparam name="TContext">
/// The <see cref="SharedKernelDbContext"/>-derived type this accessor operates over.
/// <c>EfReadRepository{TAggregate,TId}</c> only ever knows the base <see cref="SharedKernelDbContext"/>
/// type (it has no generic parameter for a consuming service's concrete <c>DbContext</c> subclass),
/// so its constructor always consumes <see cref="IReadReplicaContextAccessor{TContext}"/> closed at
/// <typeparamref name="TContext"/> = <see cref="SharedKernelDbContext"/>. The concrete registered
/// implementation (<see cref="ReadReplicaContextAccessor{TContext}"/>) is internally generic over the
/// consuming service's real, concrete context type for replica construction via
/// <c>ActivatorUtilities.CreateInstance&lt;TContext&gt;</c>, but it always implements this interface
/// closed at <see cref="SharedKernelDbContext"/> so it is resolvable by
/// <c>EfReadRepository{TAggregate,TId}</c>'s fixed constructor signature.
/// </typeparam>
/// <remarks>
/// <para>
/// A wiring detail, not a consumer-facing abstraction — declared <see langword="public"/> only
/// because it appears in <c>EfReadRepository{TAggregate,TId}</c>'s <see langword="protected"/>
/// constructor signature (C# requires a member's parameter types to be at least as accessible as
/// the member itself). No consuming service is ever expected to implement or directly reference
/// this interface — it is resolved automatically by DI, only when
/// <c>EfCorePersistenceBuilder{TContext}.WithReadReplica(...)</c> was called; when omitted, no
/// implementation is ever registered and every <c>EfReadRepository</c> constructor resolves
/// <see langword="null"/> for the corresponding optional parameter.
/// </para>
/// </remarks>
public interface IReadReplicaContextAccessor<TContext>
    where TContext : SharedKernelDbContext
{
    /// <summary>
    /// Returns <paramref name="primaryContext"/> unconditionally when
    /// <c>primaryContext.Database.CurrentTransaction</c> is non-<see langword="null"/> — reads
    /// inside an active transaction are never routed to the replica. Otherwise returns a lazily
    /// constructed, scope-cached replica context instance.
    /// </summary>
    /// <param name="primaryContext">The primary, scope-resolved <typeparamref name="TContext"/> instance.</param>
    /// <returns>The context a read operation should execute against.</returns>
    TContext GetEffectiveContext(TContext primaryContext);
}
