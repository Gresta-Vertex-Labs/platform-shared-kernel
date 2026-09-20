using AppBehaviorsIUnitOfWork = SharedKernel.Application.Behaviors.Transaction.IUnitOfWork;
using PersistenceIUnitOfWork = SharedKernel.Persistence.Abstractions.UnitOfWork.IUnitOfWork;

namespace SharedKernel.ServiceDefaults.Persistence.UnitOfWork;

/// <summary>
/// Bridges <c>06.Persistence</c>'s <see cref="PersistenceIUnitOfWork"/> to
/// <c>05.Application.Behaviors</c>'s smaller <see cref="AppBehaviorsIUnitOfWork"/> local seam, so
/// <c>TransactionBehavior</c> can commit through whichever <see cref="PersistenceIUnitOfWork"/>
/// implementation the consuming service registered.
/// </summary>
/// <remarks>
/// <para>
/// This adapter replaces the former <c>EfCorePersistenceBuilder.WithApplicationTransactionBehavior()</c>
/// registration, which resolved <c>SharedKernel.Persistence.Abstractions.IUnitOfWork</c> and cast it
/// directly to the application-layer interface — safe only when that resolved instance happened to be
/// <c>EfUnitOfWork</c> (the only type that implemented both interfaces directly). Combining
/// <c>WithTransactionalUnitOfWork()</c> with the old registration resolved an
/// <c>EfTransactionalUnitOfWork</c> instead, which never implemented the application-layer interface,
/// and the cast threw <see cref="InvalidCastException"/> at the first commit. Delegating through the
/// interface — never casting to a concrete type — fixes this for both <c>EfUnitOfWork</c> and
/// <c>EfTransactionalUnitOfWork</c>, and for any future <see cref="PersistenceIUnitOfWork"/>
/// implementation.
/// </para>
/// </remarks>
public sealed class PersistenceUnitOfWorkAdapter : AppBehaviorsIUnitOfWork
{
    private readonly PersistenceIUnitOfWork _inner;

    /// <summary>Initialises a new <see cref="PersistenceUnitOfWorkAdapter"/>.</summary>
    /// <param name="inner">
    /// The scoped <c>06.Persistence</c> unit of work to delegate to — <c>EfUnitOfWork</c> or
    /// <c>EfTransactionalUnitOfWork</c>, whichever the consuming service registered.
    /// </param>
    public PersistenceUnitOfWorkAdapter(PersistenceIUnitOfWork inner)
    {
        _inner = inner;
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _inner.SaveChangesAsync(cancellationToken);
}
