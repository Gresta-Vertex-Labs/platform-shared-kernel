using SharedKernel.Application.Behaviors.Transaction;

namespace SharedKernel.Application.Behaviors.Tests.Support;

/// <summary>
/// A minimal <see cref="IPersistenceTransaction"/> double recording commit/rollback/dispose calls,
/// optionally into a shared ordering sequence.
/// </summary>
internal sealed class FakePersistenceTransaction(List<string>? sequence = null) : IPersistenceTransaction
{
    public bool IsCommitted { get; private set; }

    public bool IsRolledBack { get; private set; }

    public bool IsDisposed { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        IsCommitted = true;
        sequence?.Add("transaction.commit");
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken)
    {
        IsRolledBack = true;
        sequence?.Add("transaction.rollback");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}
