using FluentAssertions;
using SharedKernel.ServiceDefaults.Persistence.UnitOfWork;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.ServiceDefaults.Persistence.Tests.UnitOfWork;

/// <summary>
/// Fast, database-free proof of <see cref="TransactionalPersistenceUnitOfWorkAdapter"/>'s own
/// delegation to <c>06.Persistence</c>'s real <c>ITransactionalUnitOfWork</c>/<c>IPersistenceTransaction</c>
/// shapes, using <c>16.Testing</c>'s existing fakes for both.
/// </summary>
/// <remarks>
/// The atomic commit/rollback semantics this adapter exists to enable — a business write and a
/// <c>Succeeded</c>-outcome audit record committing or rolling back together — are proven against real
/// PostgreSQL, through the unmodified production pipeline, in
/// <c>Integration/AuditTransactionWiringPostgresTests</c>. This suite proves only that the adapter
/// itself forwards every call to the instance it wraps, never swallowing or short-circuiting one.
/// </remarks>
public sealed class TransactionalPersistenceUnitOfWorkAdapterTests
{
    [Fact]
    public async Task SaveChangesAsync_DelegatesToInner_AndReturnsItsResult()
    {
        var inner = new FakeUnitOfWork { SaveChangesResult = 3 };
        var adapter = new TransactionalPersistenceUnitOfWorkAdapter(inner);

        var result = await adapter.SaveChangesAsync(CancellationToken.None);

        result.Should().Be(3);
        inner.SaveChangesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task BeginTransactionAsync_DelegatesToInner()
    {
        var inner = new FakeUnitOfWork();
        var adapter = new TransactionalPersistenceUnitOfWorkAdapter(inner);

        await using var transaction = await adapter.BeginTransactionAsync(CancellationToken.None);

        inner.TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task CommitAsync_OnTheAdaptedHandle_DelegatesToTheUnderlyingTransaction()
    {
        var inner = new FakeUnitOfWork();
        var adapter = new TransactionalPersistenceUnitOfWorkAdapter(inner);

        await using var transaction = await adapter.BeginTransactionAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        // FakePersistenceTransaction (the underlying 06.Persistence fake) throws on a SECOND
        // commit/rollback once already committed — reaching that guard through the adapted handle
        // proves CommitAsync genuinely reached the underlying instance rather than being swallowed.
        var act = async () => await transaction.RollbackAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RollbackAsync_OnTheAdaptedHandle_DelegatesToTheUnderlyingTransaction()
    {
        var inner = new FakeUnitOfWork();
        var adapter = new TransactionalPersistenceUnitOfWorkAdapter(inner);

        await using var transaction = await adapter.BeginTransactionAsync(CancellationToken.None);
        await transaction.RollbackAsync(CancellationToken.None);

        var act = async () => await transaction.CommitAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DisposeAsync_OnTheAdaptedHandle_DelegatesToTheUnderlyingTransaction()
    {
        var inner = new FakeUnitOfWork();
        var adapter = new TransactionalPersistenceUnitOfWorkAdapter(inner);

        var transaction = await adapter.BeginTransactionAsync(CancellationToken.None);
        await transaction.DisposeAsync();

        var act = async () => await transaction.CommitAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
