using SharedKernel.Persistence.Abstractions.UnitOfWork;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// In-memory fake implementation of <see cref="ITransactionalUnitOfWork"/> — and transitively
/// <see cref="IUnitOfWork"/> — (<c>06.Persistence</c>) for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberate naming collision, disambiguated only by namespace:</b> this is a DIFFERENT type from
/// <see cref="SharedKernel.Testing.Application.FakeUnitOfWork"/>, which implements
/// <c>05.Application.Behaviors</c>'s unrelated, single-member local <c>IUnitOfWork</c> seam. The two
/// types happen to share a simple name because the two interfaces they fake happen to share a simple
/// name — the same collision the root <c>CLAUDE.md</c> already documents between
/// <c>05.Application.Behaviors.IUnitOfWork</c> and <c>SharedKernel.Persistence.Abstractions.IUnitOfWork</c>.
/// This type satisfies the <c>06.Persistence</c> contract only; see
/// <see cref="SharedKernel.Testing.Application.FakeUnitOfWork"/> for the <c>05.Application</c> one.
/// </para>
/// <para>
/// Fully INDEPENDENT of <see cref="FakeRepository{TAggregate, TId}"/> — no constructor coupling.
/// <see cref="SaveChangesAsync"/> is a pure counter with no interaction with any
/// <see cref="FakeRepository{TAggregate, TId}"/> instance, since every write on that type is applied
/// immediately with no <c>ChangeTracker</c>-style staging to "save."
/// </para>
/// </remarks>
public sealed class FakeUnitOfWork : ITransactionalUnitOfWork
{
    private int _saveChangesCallCount;
    private int _transactionCount;

    /// <summary>Gets the number of times <see cref="SaveChangesAsync"/> has been called.</summary>
    public int SaveChangesCallCount => _saveChangesCallCount;

    /// <summary>Gets or sets the value <see cref="SaveChangesAsync"/> returns on success. Defaults to <c>0</c>.</summary>
    public int SaveChangesResult { get; set; }

    /// <summary>
    /// Gets or sets whether <see cref="SaveChangesAsync"/> should throw
    /// <see cref="InvalidOperationException"/> instead of returning.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>Gets the number of times <see cref="BeginTransactionAsync"/> has been called.</summary>
    public int TransactionCount => _transactionCount;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when <see cref="SimulateFailure"/> is <see langword="true"/>.</exception>
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _saveChangesCallCount);

        if (SimulateFailure)
            throw new InvalidOperationException("FakeUnitOfWork.SaveChangesAsync was configured to simulate failure.");

        return Task.FromResult(SaveChangesResult);
    }

    /// <inheritdoc />
    /// <remarks>Returns a freshly-constructed <see cref="FakePersistenceTransaction"/> on every call.</remarks>
    public Task<IPersistenceTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _transactionCount);
        return Task.FromResult<IPersistenceTransaction>(new FakePersistenceTransaction());
    }

    /// <inheritdoc />
    /// <remarks>
    /// Invokes <paramref name="operation"/> exactly once — no retry — and propagates its result or
    /// exception directly. A deliberate, documented divergence from the real contract's own "the
    /// operation delegate may run more than once" retrying-execution-strategy note.
    /// </remarks>
    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation(ct);
    }

    /// <inheritdoc />
    /// <remarks>Same no-retry contract as the non-generic overload — see its remarks.</remarks>
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation(ct);
    }

    /// <summary>
    /// Clears <see cref="SaveChangesCallCount"/> and <see cref="TransactionCount"/>, and resets
    /// <see cref="SaveChangesResult"/> to <c>0</c> and <see cref="SimulateFailure"/> to <see langword="false"/>.
    /// </summary>
    public void Reset()
    {
        _saveChangesCallCount = 0;
        _transactionCount = 0;
        SaveChangesResult = 0;
        SimulateFailure = false;
    }
}
