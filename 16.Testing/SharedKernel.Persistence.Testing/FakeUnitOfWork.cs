using System.Data;
using System.Threading;
using SharedKernel.Application.Transactions;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Persistence.Testing;

/// <summary>
/// In-memory fake implementation of the shared <see cref="IUnitOfWork"/>
/// (<c>SharedKernel.Application.Abstractions</c>) for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// P-558: one fake for the one unit-of-work contract the application pipeline and the persistence
/// layer now share (the former <c>SharedKernel.Application</c> and <c>06.Persistence</c> copies, and
/// their two same-named fakes, are gone).
/// </para>
/// <para>
/// Follows the contract's transaction rules without a database: an
/// <c>ExecuteInTransactionAsync</c> call runs the operation, saves, runs every
/// <see cref="OnBeforeCommit"/> callback and commits; an exception or a failed <c>Result</c> rolls
/// back. <see cref="TransientFailures"/> simulates a retrying execution strategy replaying the
/// operation, so a test can prove its handler is re-runnable. A call made while a transaction is
/// already active joins it. <see cref="SaveChangesAsync"/> is a pure counter — it stages and writes
/// nothing.
/// </para>
/// <para>
/// A rollback undoes the writes of every <see cref="FakeRepository{TAggregate, TId}"/> registered next to it with
/// <c>AddFakeRepository</c>: each is put back as it was when the transaction started, so a replayed handler finds
/// the state a real retry would find, and a failed command leaves nothing behind.
/// </para>
/// </remarks>
#pragma warning disable RS0026 // Mirrors IUnitOfWork's overload set.
public sealed class FakeUnitOfWork : IUnitOfWork
{
    private readonly List<Func<CancellationToken, Task>> _beforeCommit = [];
    private readonly List<IFakeTransactionParticipant> _participants = [];
    private readonly List<object> _snapshots = [];
    private int _saveChangesCallCount;
    private int _depth;

    /// <summary>Gets the number of times <see cref="SaveChangesAsync"/> has been called (including the implicit save of a transaction), thread-safe.</summary>
    public int SaveChangesCallCount => Volatile.Read(ref _saveChangesCallCount);

    /// <summary>Gets or sets the value <see cref="SaveChangesAsync"/> returns on success. Defaults to <c>1</c>.</summary>
    public int SaveChangesResult { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether <see cref="SaveChangesAsync"/> should throw
    /// <see cref="InvalidOperationException"/> instead of returning. <see cref="SaveChangesCallCount"/>
    /// still increments — the call happened, it just faulted.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>Gets or sets how many attempts fail with a simulated transient error before one commits.</summary>
    public int TransientFailures { get; set; }

    /// <summary>Gets the number of transaction attempts started (retries included).</summary>
    public int TransactionCount { get; private set; }

    /// <summary>Gets the number of committed transactions.</summary>
    public int CommitCount { get; private set; }

    /// <summary>Gets the number of rolled-back transaction attempts.</summary>
    public int RollbackCount { get; private set; }

    /// <inheritdoc />
    public bool IsTransactionActive => _depth > 0;

    private bool _rollbackOnly;

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _saveChangesCallCount);

        if (SimulateFailure)
            throw new InvalidOperationException("FakeUnitOfWork.SaveChangesAsync was configured to simulate failure.");

        return Task.FromResult(SaveChangesResult);
    }

    /// <inheritdoc />
    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync(operation, isolationLevel: null, cancellationToken);

    /// <inheritdoc />
    /// <remarks>Ignores <paramref name="isolationLevel"/> — this fake never issues SQL.</remarks>
    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ExecuteInTransactionAsync<object?>(
            async ct =>
            {
                await operation(ct);
                return null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>Ignores <paramref name="isolationLevel"/> — this fake never issues SQL.</remarks>
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync(operation, cancellationToken);

    /// <inheritdoc />
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (IsTransactionActive)
        {
            // Joined, like the real unit of work: a failure marks the transaction rollback-only.
            TResult joined;
            try
            {
                joined = await operation(cancellationToken);
            }
            catch
            {
                _rollbackOnly = true;
                throw;
            }

            if (joined is IHasSuccessFlag { IsSuccess: false })
                _rollbackOnly = true;
            else
                await SaveChangesAsync(cancellationToken);

            return joined;
        }

        while (true)
        {
            TransactionCount++;
            _beforeCommit.Clear();
            _snapshots.Clear();
            foreach (var participant in _participants)
                _snapshots.Add(participant.Capture());
            _depth++;
            _rollbackOnly = false;

            try
            {
                var result = await operation(cancellationToken);

                if (result is IHasSuccessFlag { IsSuccess: false })
                {
                    Rollback();
                    return result;
                }

                if (_rollbackOnly)
                {
                    Rollback();
                    throw new TransactionRolledBackException();
                }

                await SaveChangesAsync(cancellationToken);

                if (TransientFailures > 0)
                {
                    TransientFailures--;
                    Rollback();
                    continue;
                }

                for (var i = 0; i < _beforeCommit.Count; i++)
                    await _beforeCommit[i](cancellationToken);

                CommitCount++;
                _depth--;
                _beforeCommit.Clear();
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

    /// <inheritdoc />
    public void OnBeforeCommit(Func<CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (!IsTransactionActive)
            throw new InvalidOperationException("OnBeforeCommit can only be called while ExecuteInTransactionAsync is running.");

        _beforeCommit.Add(callback);
    }

    /// <summary>
    /// Clears every counter and resets <see cref="SaveChangesResult"/> to <c>1</c>,
    /// <see cref="SimulateFailure"/> to <see langword="false"/> and <see cref="TransientFailures"/> to <c>0</c>.
    /// </summary>
    public void Reset()
    {
        Volatile.Write(ref _saveChangesCallCount, 0);
        TransactionCount = 0;
        CommitCount = 0;
        RollbackCount = 0;
        SaveChangesResult = 1;
        SimulateFailure = false;
        TransientFailures = 0;
        _beforeCommit.Clear();
        _depth = 0;
    }

    /// <summary>Makes <paramref name="participant"/>'s writes part of this unit of work's transactions (done by the <c>Add*</c> helpers).</summary>
    internal void Enlist(IFakeTransactionParticipant participant)
    {
        if (!_participants.Contains(participant))
            _participants.Add(participant);
    }

    private void Rollback()
    {
        RollbackCount++;
        _depth--;
        _beforeCommit.Clear();

        // What the database rollback and the change-tracker reset do for the real unit of work.
        for (var i = 0; i < _snapshots.Count; i++)
            _participants[i].Restore(_snapshots[i]);
    }
}
#pragma warning restore RS0026
