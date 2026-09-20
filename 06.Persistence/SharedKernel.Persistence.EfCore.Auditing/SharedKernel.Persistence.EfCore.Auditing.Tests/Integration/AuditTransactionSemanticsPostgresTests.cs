using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Auditing.Chain;
using SharedKernel.Persistence.EfCore.Auditing.Tests.TestFixtures;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>
/// Proves <c>IAuditTrailWriter.RecordAsync</c>'s exact transaction-semantics rule (see its own
/// remarks) against real PostgreSQL: a SUCCEEDED entry commits/rolls back atomically WITH the
/// caller's own business write (same connection/transaction), while a FAILED entry ALWAYS persists
/// independently, even when the business write it describes rolls back.
/// </summary>
[Collection("AuditPostgres")]
public sealed class AuditTransactionSemanticsPostgresTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public AuditTransactionSemanticsPostgresTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = database }.ConnectionString;

    private static async Task<ServiceProvider> BuildAndCreateAsync(string connectionString, Guid? tenantId = null)
    {
        var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext(tenantId: tenantId ?? Guid.NewGuid()));
        await using var scope = sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();
        return sp;
    }

    [Fact]
    public async Task SucceededOutcome_AmbientTransactionCommits_BusinessRowAndAuditRecordBothPersist()
    {
        var connectionString = ConnectionString("sk_audit_tx_success_commit");
        await using var sp = await BuildAndCreateAsync(connectionString);

        var orderId = Guid.NewGuid();

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();

            await using var tx = await unitOfWork.BeginTransactionAsync();

            context.Orders.Add(new AuditTestOrder(orderId, "atomic-success-order"));
            await context.SaveChangesAsync();

            await writer.RecordAsync(new AuditEntry
            {
                Action = "OrderCreated",
                ResourceType = "Order",
                ResourceId = orderId.ToString(),
                Outcome = AuditOutcome.Succeeded,
            });

            await tx.CommitAsync();
        }

        await using var verifyScope = sp.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();
        (await verifyContext.Orders.CountAsync()).Should().Be(1);
        (await verifyContext.Set<AuditRecord>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SucceededOutcome_AmbientTransactionRollsBack_BusinessRowAndAuditRecordBothVanish()
    {
        var connectionString = ConnectionString("sk_audit_tx_success_rollback");
        await using var sp = await BuildAndCreateAsync(connectionString);

        var orderId = Guid.NewGuid();

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();

            await using var tx = await unitOfWork.BeginTransactionAsync();

            context.Orders.Add(new AuditTestOrder(orderId, "atomic-rollback-order"));
            await context.SaveChangesAsync();

            // Outcome=Succeeded enlists in the SAME ambient transaction as the business write above —
            // if that transaction later rolls back for any reason, the "succeeded" audit record must
            // vanish with it: the business action it describes did not, in the end, actually happen.
            await writer.RecordAsync(new AuditEntry
            {
                Action = "OrderCreated",
                ResourceType = "Order",
                ResourceId = orderId.ToString(),
                Outcome = AuditOutcome.Succeeded,
            });

            await tx.RollbackAsync();
        }

        await using var verifyScope = sp.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();
        (await verifyContext.Orders.CountAsync()).Should().Be(0, "the business transaction rolled back");
        (await verifyContext.Set<AuditRecord>().CountAsync()).Should().Be(0, "a Succeeded-outcome audit record enlisted in, and rolls back with, the same ambient transaction");
    }

    [Fact]
    public async Task FailedOutcome_BusinessTransactionRollsBack_AuditRecordStillPersists()
    {
        var connectionString = ConnectionString("sk_audit_tx_failure_persists");
        await using var sp = await BuildAndCreateAsync(connectionString);

        var orderId = Guid.NewGuid();

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();

            await using var tx = await unitOfWork.BeginTransactionAsync();

            // A command that ultimately fails may still have staged a partial, never-committed
            // business write before the failure was detected.
            context.Orders.Add(new AuditTestOrder(orderId, "never-committed-order"));
            await context.SaveChangesAsync();

            // Outcome=Failed ALWAYS gets its own independent connection/transaction, committed
            // immediately — never contingent on what happens to the ambient business transaction
            // above.
            await writer.RecordAsync(new AuditEntry
            {
                Action = "OrderCreationRejected",
                ResourceType = "Order",
                ResourceId = orderId.ToString(),
                Outcome = AuditOutcome.Failed,
                ErrorCode = "order.validation_failed",
            });

            await tx.RollbackAsync();
        }

        await using var verifyScope = sp.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();
        (await verifyContext.Orders.CountAsync()).Should().Be(0, "the business transaction rolled back — the order was never actually created");

        var auditRecords = await verifyContext.Set<AuditRecord>().ToListAsync();
        auditRecords.Should().ContainSingle();
        auditRecords[0].Outcome.Should().Be(AuditOutcome.Failed);
        auditRecords[0].ErrorCode.Should().Be("order.validation_failed");
    }

    [Fact]
    public async Task SucceededOutcome_NoAmbientTransactionAtAll_ThrowsAndWritesNothing()
    {
        // C2 regression: a Succeeded-outcome attestation with NO transactional tie to the business
        // write it describes must never be allowed to stand alone — see EfAuditTrailWriter's remarks.
        var connectionString = ConnectionString("sk_audit_tx_succeeded_no_ambient_refused");
        await using var sp = await BuildAndCreateAsync(connectionString);

        await using var scope = sp.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();

        var act = async () => await writer.RecordAsync(new AuditEntry
        {
            Action = "OrderCreated",
            ResourceType = "Order",
            ResourceId = "order-standalone",
            Outcome = AuditOutcome.Succeeded,
        });

        await act.Should().ThrowAsync<InvalidOperationException>();

        await using var verifyScope = sp.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();
        (await verifyContext.Set<AuditRecord>().CountAsync()).Should().Be(
            0, "the refusal must be total — no partial/orphaned record from the rejected attempt");
    }

    [Fact]
    public async Task FailedOutcome_SameChainAsAmbientSucceededTransaction_TimesOutRatherThanHangingForever()
    {
        // H8 regression: a Succeeded entry enlisted in an ambient transaction holds that chain's
        // advisory lock for the transaction's whole remaining lifetime. A Failed entry on the SAME
        // chain, from the same logical request, opens its OWN connection and — pre-fix — blocks on
        // that lock FOREVER (the ambient transaction is idle-in-transaction awaiting application code,
        // so Postgres's own deadlock detector never fires). AdvisoryLockTimeout bounds the wait.
        var connectionString = ConnectionString("sk_audit_tx_deadlock_bounded");
        var tenantId = Guid.NewGuid();

        var sp = AuditTestHost.Build(
            connectionString, new FakeAuditActorContext(tenantId: tenantId),
            configureServices: services => services.PostConfigure<AuditChainOptions>(
                o => o.AdvisoryLockTimeout = TimeSpan.FromSeconds(2)));
        await using (sp)
        {
            await using (var scope = sp.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();

            await using var scope1 = sp.CreateAsyncScope();
            var context = scope1.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();
            var unitOfWork = scope1.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
            var writer1 = scope1.ServiceProvider.GetRequiredService<IAuditTrailWriter>();

            await using var tx = await unitOfWork.BeginTransactionAsync();

            var orderId = Guid.NewGuid();
            context.Orders.Add(new AuditTestOrder(orderId, "deadlock-probe-order"));
            await context.SaveChangesAsync();

            // Holds chain (tenantId, "Order")'s advisory lock for the rest of THIS transaction.
            await writer1.RecordAsync(new AuditEntry
            {
                Action = "OrderCreated",
                ResourceType = "Order",
                ResourceId = orderId.ToString(),
                Outcome = AuditOutcome.Succeeded,
            });

            // Still inside tx1 — a nested Failed write on the SAME chain opens its own connection and
            // contends for the same lock.
            await using var scope2 = sp.CreateAsyncScope();
            var writer2 = scope2.ServiceProvider.GetRequiredService<IAuditTrailWriter>();

            var recordTask = writer2.RecordAsync(new AuditEntry
            {
                Action = "SubOperationRejected",
                ResourceType = "Order",
                ResourceId = orderId.ToString(),
                Outcome = AuditOutcome.Failed,
                ErrorCode = "sub.failed",
            });

            var completed = await Task.WhenAny(recordTask, Task.Delay(TimeSpan.FromSeconds(20)));
            completed.Should().Be(recordTask, "AdvisoryLockTimeout must fail the call within a bounded time, never hang forever");

            var act = async () => await recordTask;
            (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.LockNotAvailable);

            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task FailedOutcome_NoAmbientTransactionAtAll_StillPersists()
    {
        var connectionString = ConnectionString("sk_audit_tx_failure_no_ambient");
        await using var sp = await BuildAndCreateAsync(connectionString);

        await using var scope = sp.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();

        var record = await writer.RecordAsync(new AuditEntry
        {
            Action = "Rejected",
            ResourceType = "Order",
            ResourceId = "order-standalone",
            Outcome = AuditOutcome.Failed,
            ErrorCode = "order.rejected",
        });

        await using var verifyScope = sp.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();
        (await verifyContext.Set<AuditRecord>().SingleAsync(r => r.Id == record.Id)).Should().NotBeNull();
    }
}
