using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
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
