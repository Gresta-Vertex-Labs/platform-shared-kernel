using SharedKernel.Persistence.Abstractions.UnitOfWork;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// In-memory fake implementation of <see cref="IPersistenceTransaction"/> (<c>06.Persistence</c>) for
/// use in unit tests.
/// </summary>
/// <remarks>
/// Returned exclusively by <see cref="FakeUnitOfWork.BeginTransactionAsync"/>. Never constructed
/// directly by test code.
/// </remarks>
public sealed class FakePersistenceTransaction : IPersistenceTransaction
{
    /// <summary>Gets a value indicating whether <see cref="CommitAsync"/> has already succeeded.</summary>
    public bool IsCommitted { get; private set; }

    /// <summary>Gets a value indicating whether <see cref="RollbackAsync"/> has already succeeded.</summary>
    public bool IsRolledBack { get; private set; }

    /// <summary>Gets a value indicating whether <see cref="DisposeAsync"/> has already been called.</summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Thrown when the transaction has already been committed, rolled back, or disposed.
    /// </exception>
    public Task CommitAsync(CancellationToken ct = default)
    {
        ThrowIfFinal();
        IsCommitted = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Thrown when the transaction has already been committed, rolled back, or disposed.
    /// </exception>
    public Task RollbackAsync(CancellationToken ct = default)
    {
        ThrowIfFinal();
        IsRolledBack = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Idempotent — a second call is a silent no-op.</remarks>
    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    private void ThrowIfFinal()
    {
        if (IsCommitted)
            throw new InvalidOperationException("The transaction has already been committed.");

        if (IsRolledBack)
            throw new InvalidOperationException("The transaction has already been rolled back.");

        if (IsDisposed)
            throw new InvalidOperationException("The transaction has already been disposed.");
    }
}
