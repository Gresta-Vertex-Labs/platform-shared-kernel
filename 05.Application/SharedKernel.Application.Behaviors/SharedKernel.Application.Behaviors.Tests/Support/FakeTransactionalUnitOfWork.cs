using SharedKernel.Application.Behaviors.Transaction;

namespace SharedKernel.Application.Behaviors.Tests.Support;

/// <summary>
/// A minimal <see cref="ITransactionalUnitOfWork"/> double recording begin/save-changes calls,
/// optionally into a shared ordering sequence, and exposing the last transaction handle it produced.
/// </summary>
internal sealed class FakeTransactionalUnitOfWork(List<string>? sequence = null) : ITransactionalUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public int BeginTransactionCallCount { get; private set; }

    public bool SimulateSaveChangesFailure { get; set; }

    public FakePersistenceTransaction? LastTransaction { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;
        sequence?.Add("transaction.savechanges");

        if (SimulateSaveChangesFailure)
        {
            throw new InvalidOperationException(
                "FakeTransactionalUnitOfWork.SaveChangesAsync was configured to simulate failure.");
        }

        return Task.FromResult(1);
    }

    public Task<IPersistenceTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        BeginTransactionCallCount++;
        sequence?.Add("transaction.begin");
        LastTransaction = new FakePersistenceTransaction(sequence);
        return Task.FromResult<IPersistenceTransaction>(LastTransaction);
    }
}
