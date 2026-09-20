using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.Postgres;

/// <summary>
/// Proves <c>EfTransactionalUnitOfWork.ExecuteInTransactionAsync</c>
/// retry safety against REAL PostgreSQL under a genuine injected transient fault
/// (<see cref="TransientFaultInjectionInterceptor"/>): a transient failure followed by a successful
/// retry leaves exactly ONE row behind (never double-applied), and the <c>verifySucceeded</c> hook
/// correctly gates whether a retry-exhaustion failure propagates.
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
        Guid tenantId, int? maxRetryCount, TransientFaultInjectionInterceptor? faultInjector = null) =>
        PgTestDbContextFactory.Create(
            ConnectionString,
            new FakeAuditActorContext("actor"),
            new FakeAuditActorContext("actor", tenantId),
            maxRetryCount: maxRetryCount,
            maxRetryDelay: TimeSpan.FromMilliseconds(20),
            providerLevelInterceptors: faultInjector is null ? null : [faultInjector]);

    [Fact]
    public async Task ExecuteInTransactionAsync_TransientFailureThenSuccess_DoesNotDoubleApply()
    {
        var tenantId = Guid.NewGuid();
        var code = $"retry-success-{Guid.NewGuid():N}";
        var faultInjector = new TransientFaultInjectionInterceptor(failuresBeforeSuccess: 2);

        await using (var setup = CreateContext(tenantId, maxRetryCount: null))
            await setup.Database.EnsureCreatedAsync();

        await using var ctx = CreateContext(tenantId, maxRetryCount: 5, faultInjector);
        var uow = new EfTransactionalUnitOfWork(ctx);

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
            "ChangeTracker.Clear() at the start of every attempt must mean exactly ONE row exists, never a duplicate from an earlier failed attempt");
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_RetryExhausted_VerifySucceededFalse_Rethrows()
    {
        var tenantId = Guid.NewGuid();
        var code = $"retry-exhaust-false-{Guid.NewGuid():N}";
        // Always fails — every attempt within the budget is injected as transient, so the retrying
        // execution strategy must eventually exhaust and throw RetryLimitExceededException.
        var faultInjector = new TransientFaultInjectionInterceptor(failuresBeforeSuccess: int.MaxValue);

        await using (var setup = CreateContext(tenantId, maxRetryCount: null))
            await setup.Database.EnsureCreatedAsync();

        await using var ctx = CreateContext(tenantId, maxRetryCount: 1, faultInjector);
        var uow = new EfTransactionalUnitOfWork(ctx);

        var act = () => uow.ExecuteInTransactionAsync(
            async token =>
            {
                ctx.Orders.Add(new PgOrderAggregate(
                    PgOrderId.New(), tenantId, "NeverCommits", code, "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
                await Task.CompletedTask;
            },
            isolationLevel: null,
            verifySucceeded: _ => Task.FromResult(false));

        await act.Should().ThrowAsync<RetryLimitExceededException>(
            "verifySucceeded returning false must let the exhaustion failure propagate");

        await using var verifyCtx = CreateContext(tenantId, maxRetryCount: null);
        (await verifyCtx.Orders.CountAsync(o => o.Code == code)).Should().Be(0,
            "every attempt was injected to fail before reaching the server — nothing should have been written");
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_RetryExhausted_VerifySucceededTrue_SwallowsFailure()
    {
        var tenantId = Guid.NewGuid();
        var code = $"retry-exhaust-true-{Guid.NewGuid():N}";
        var faultInjector = new TransientFaultInjectionInterceptor(failuresBeforeSuccess: int.MaxValue);

        await using (var setup = CreateContext(tenantId, maxRetryCount: null))
            await setup.Database.EnsureCreatedAsync();

        await using var ctx = CreateContext(tenantId, maxRetryCount: 1, faultInjector);
        var uow = new EfTransactionalUnitOfWork(ctx);
        var verifySucceededCalled = false;

        var act = () => uow.ExecuteInTransactionAsync(
            async token =>
            {
                ctx.Orders.Add(new PgOrderAggregate(
                    PgOrderId.New(), tenantId, "VerifiedSucceeded", code, "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
                await Task.CompletedTask;
            },
            isolationLevel: null,
            verifySucceeded: _ =>
            {
                verifySucceededCalled = true;
                return Task.FromResult(true);
            });

        await act.Should().NotThrowAsync(
            "verifySucceeded returning true must swallow the retry-exhaustion failure, trusting the caller's own idempotency check");
        verifySucceededCalled.Should().BeTrue("verifySucceeded must actually be invoked once retries are exhausted");
    }
}
