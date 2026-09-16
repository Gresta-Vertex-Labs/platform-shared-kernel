using SharedKernel.Application.Behaviors.Transaction;

namespace SharedKernel.Application.Behaviors.Tests.Support;

/// <summary>A minimal <see cref="IUnitOfWork"/> double recording every commit call.</summary>
internal sealed class FakeUnitOfWork(List<string>? sequence = null) : IUnitOfWork
{
    public int CommitCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        CommitCount++;
        sequence?.Add("transaction.commit");
        return Task.FromResult(1);
    }
}
