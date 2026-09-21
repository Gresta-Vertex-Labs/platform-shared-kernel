using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.Postgres;

/// <summary>
/// Proves <c>EfUnitOfWork.ExecuteInTransactionAsync</c> retry safety against REAL PostgreSQL under a
/// genuine injected transient fault (<see cref="TransientFaultInjectionInterceptor"/>): a transient
/// failure followed by a successful retry leaves exactly ONE row behind (never double-applied), and an
/// exhausted retry budget propagates and writes nothing.
/// </summary>
[Collection("EfCorePostgres")]
public sealed class TransactionRetryPostgresTests
{
    private const string DatabaseName = "sk_p557_transaction_retry";

    private readonly PostgreSqlContainerFixture _fixture;

    public TransactionRetryPostgresTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = DatabaseName }.ConnectionString;

    private PgTestDbContext CreateContext(
        Guid tenantId, int? maxRetryCount, TransientFaultInjectionInterceptor? faultInjector = null)
    {
        var caller = new FakeAuditActorContext("actor", tenantId);
        return PgTestDbContextFactory.Create(
            ConnectionString,
            caller,
            caller,
            maxRetryCount: maxRetryCount,
            maxRetryDelay: TimeSpan.FromMilliseconds(20),
            providerLevelInterceptors: faultInjector is null ? null : [faultInjector]);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_TransientFailureThenSuccess_DoesNotDoubleApply()
    {
        var tenantId = Guid.NewGuid();
        var code = $"retry-success-{Guid.NewGuid():N}";
        var faultInjector = new TransientFaultInjectionInterceptor(failuresBeforeSuccess: 2);

        await using (var setup = CreateContext(tenantId, maxRetryCount: null))
            await setup.Database.EnsureCreatedAsync();

        await using var ctx = CreateContext(tenantId, maxRetryCount: 5, faultInjector);
        var uow = new EfUnitOfWork(ctx);

        await uow.ExecuteInTransactionAsync(async token =>
        {
            ctx.Orders.Add(new PgOrderAggregate(
                PgOrderId.New(), tenantId, "Retried", code, "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await Task.CompletedTask;
        });

        faultInjector.AttemptsObserved.Should().Be(3,
            "the first two attempts must have been injected as transient failures, the third genuinely succeeding");

        await using var verifyCtx = CreateContext(tenantId, maxRetryCount: null);
        var matching = await verifyCtx.Orders.Where(o => o.Code == code).ToListAsync();
        matching.Should().ContainSingle(
            "clearing the change tracker before every retried attempt must mean exactly ONE row exists");
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_RetryExhausted_RethrowsAndWritesNothing()
    {
        var tenantId = Guid.NewGuid();
        var code = $"retry-exhaust-{Guid.NewGuid():N}";
        // Always fails — every attempt within the budget is injected as transient, so the retrying
        // execution strategy must eventually exhaust and throw RetryLimitExceededException.
        var faultInjector = new TransientFaultInjectionInterceptor(failuresBeforeSuccess: int.MaxValue);

        await using (var setup = CreateContext(tenantId, maxRetryCount: null))
            await setup.Database.EnsureCreatedAsync();

        await using var ctx = CreateContext(tenantId, maxRetryCount: 1, faultInjector);
        var uow = new EfUnitOfWork(ctx);

        var act = () => uow.ExecuteInTransactionAsync(async token =>
        {
            ctx.Orders.Add(new PgOrderAggregate(
                PgOrderId.New(), tenantId, "NeverCommits", code, "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await Task.CompletedTask;
        });

        await act.Should().ThrowAsync<RetryLimitExceededException>();

        await using var verifyCtx = CreateContext(tenantId, maxRetryCount: null);
        (await verifyCtx.Orders.CountAsync(o => o.Code == code)).Should().Be(0,
            "every attempt was injected to fail before reaching the server — nothing should have been written");
    }
}
