using System.Threading;
using SharedKernel.Application.Behaviors.Transaction;

namespace SharedKernel.Testing.Application;

/// <summary>
/// In-memory fake implementation of <see cref="IUnitOfWork"/> (<c>05.Application.Behaviors</c>) for
/// use in unit tests.
/// </summary>
/// <remarks>
/// This is <b>not</b> a fake for <c>SharedKernel.Persistence.Abstractions.IUnitOfWork</c>
/// (<c>06.Persistence</c>) — <c>05.Application</c> ships its own, deliberately narrower local
/// <see cref="IUnitOfWork"/> seam (a single <see cref="SaveChangesAsync"/> member), bridged to the
/// real persistence <c>IUnitOfWork</c> only at each consuming service's composition root. This fake
/// satisfies the LOCAL seam only. Lets a test assert <c>TransactionBehavior</c>'s exact contract —
/// <see cref="SaveChangesAsync"/> is called exactly once after <c>next()</c> returns, never called
/// if <c>next()</c> throws — without a real persistence provider.
/// </remarks>
/// <remarks>
/// Local-seam-only scope: this type fakes <c>05.Application.Behaviors</c>' own <see cref="IUnitOfWork"/>
/// exclusively and never references <c>06.Persistence</c>, <c>12.Security</c>, or <c>07.Messaging</c> —
/// bridging the local seam to a real persistence provider is a decision made only at each consuming
/// service's composition root, never inside this package.
/// </remarks>
/// <remarks>
/// Deliberate naming collision, disambiguated only by namespace: a DIFFERENT, unrelated type,
/// <see cref="SharedKernel.Testing.Persistence.FakeUnitOfWork"/> (P-335/WO-053), implements
/// <c>SharedKernel.Persistence.Abstractions.ITransactionalUnitOfWork</c>/<c>IUnitOfWork</c>
/// (<c>06.Persistence</c>) — the two types share a simple name because the two interfaces they fake
/// happen to share a simple name.
/// </remarks>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    private int _saveChangesCallCount;

    /// <summary>Gets the number of times <see cref="SaveChangesAsync"/> has been called, thread-safe.</summary>
    public int SaveChangesCallCount => Volatile.Read(ref _saveChangesCallCount);

    /// <summary>Gets or sets the value <see cref="SaveChangesAsync"/> returns on success. Defaults to <c>1</c>.</summary>
    public int SaveChangesResult { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether <see cref="SaveChangesAsync"/> should throw
    /// <see cref="InvalidOperationException"/> instead of returning. <see cref="SaveChangesCallCount"/>
    /// still increments — the call happened, it just faulted.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _saveChangesCallCount);

        if (SimulateFailure)
            throw new InvalidOperationException("FakeUnitOfWork.SaveChangesAsync was configured to simulate failure.");

        return Task.FromResult(SaveChangesResult);
    }
}
