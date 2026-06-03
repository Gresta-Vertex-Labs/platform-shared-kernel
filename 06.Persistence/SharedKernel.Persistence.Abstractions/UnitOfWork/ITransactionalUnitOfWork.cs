namespace SharedKernel.Persistence.Abstractions.UnitOfWork;

/// <summary>
/// Extends <see cref="IUnitOfWork"/> with explicit transaction control.
/// </summary>
/// <remarks>
/// <para>
/// Register this interface only for services that require explicit transaction boundaries —
/// for example, saga compensation patterns or two-phase read-then-write operations where multiple
/// repository calls must be atomic. Services that do not need explicit transactions use
/// <see cref="IUnitOfWork"/> directly.
/// </para>
/// <para>
/// The EF Core implementation (<c>EfTransactionalUnitOfWork</c>) is registered via
/// <c>EfCorePersistenceBuilder.WithTransactionalUnitOfWork()</c>. When that method is called,
/// both <c>IUnitOfWork</c> and <c>ITransactionalUnitOfWork</c> resolve the same scoped instance.
/// </para>
/// <para>
/// <strong>Hard violation:</strong> Application-layer code must inject
/// <c>ITransactionalUnitOfWork</c> — never <c>IDbContextTransaction</c> directly. Injecting
/// the EF Core type couples application code to the ORM and bypasses the abstraction.
/// </para>
/// </remarks>
public interface ITransactionalUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Opens an explicit database transaction and returns a provider-agnostic transaction handle.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IPersistenceTransaction"/> that represents the active transaction.
    /// Call <see cref="IPersistenceTransaction.CommitAsync"/> to commit or
    /// <see cref="IPersistenceTransaction.RollbackAsync"/> to roll back.
    /// Always <c>await using</c> the returned handle to release database resources.
    /// </returns>
    /// <remarks>
    /// Multiple repository operations within the returned transaction scope are committed
    /// atomically via <see cref="IPersistenceTransaction.CommitAsync"/>. Domain event dispatch
    /// fires after <c>CommitAsync</c>, consistent with <c>EfUnitOfWork.SaveChangesAsync</c>
    /// semantics.
    /// </remarks>
    Task<IPersistenceTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
