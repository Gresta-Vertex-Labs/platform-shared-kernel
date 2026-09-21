using SharedKernel.Application.Transactions;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Support;

/// <summary>
/// An in-memory <see cref="IUnitOfWork"/> double that follows the contract's transaction rules —
/// save, pre-commit callbacks, commit; roll back on an exception or a failed result — and records
/// each step, optionally into a shared ordering sequence.
/// </summary>
internal sealed class FakeUnitOfWork(List<string>? sequence = null) : IUnitOfWork
{
    private readonly List<Func<CancellationToken, Task>> _beforeCommit = [];
    private int _depth;

    public int SaveChangesCallCount { get; private set; }

    public int CommitCount { get; private set; }

    public int RollbackCount { get; private set; }

    public int AttemptCount { get; private set; }

    /// <summary>Gets or sets how many attempts fail with a simulated transient error before one succeeds.</summary>
    public int TransientFailures { get; set; }

    public bool SimulateSaveChangesFailure { get; set; }

    public bool SimulateCommitFailure { get; set; }

    public bool IsTransactionActive => _depth > 0;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        sequence?.Add("transaction.savechanges");

        if (SimulateSaveChangesFailure)
            throw new InvalidOperationException("FakeUnitOfWork.SaveChangesAsync was configured to simulate failure.");

        return Task.FromResult(1);
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync<object?>(async ct => { await operation(ct); return null; }, cancellationToken);

    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        System.Data.IsolationLevel? isolationLevel,
        CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync(operation, cancellationToken);

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        System.Data.IsolationLevel? isolationLevel,
        CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync(operation, cancellationToken);

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        if (IsTransactionActive)
            return await operation(cancellationToken);

        while (true)
        {
            AttemptCount++;
            _beforeCommit.Clear();
            _depth++;
            sequence?.Add("transaction.begin");

            try
            {
                var result = await operation(cancellationToken);

                if (result is IHasSuccessFlag { IsSuccess: false })
                {
                    Rollback();
                    return result;
                }

                await SaveChangesAsync(cancellationToken);

                if (TransientFailures > 0)
                {
                    TransientFailures--;
                    Rollback();
                    sequence?.Add("transaction.retry");
                    continue;
                }

                foreach (var callback in _beforeCommit.ToList())
                    await callback(cancellationToken);

                if (SimulateCommitFailure)
                    throw new InvalidOperationException("FakeUnitOfWork commit was configured to simulate failure.");

                CommitCount++;
                sequence?.Add("transaction.commit");
                _depth--;
                return result;
            }
            catch
            {
                if (_depth > 0)
                    Rollback();

                throw;
            }
        }
    }

    public void OnBeforeCommit(Func<CancellationToken, Task> callback)
    {
        if (!IsTransactionActive)
            throw new InvalidOperationException("No transaction is active.");

        _beforeCommit.Add(callback);
    }

    private void Rollback()
    {
        RollbackCount++;
        _depth--;
        _beforeCommit.Clear();
        sequence?.Add("transaction.rollback");
    }
}
