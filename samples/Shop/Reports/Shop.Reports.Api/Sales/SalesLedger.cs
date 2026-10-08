using Dapper;
using Npgsql;
using SharedKernel.Contracts.Events;
using SharedKernel.Execution.Context;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.Dapper.Sessions;
using SharedKernel.Persistence.Npgsql.Connections;
using Shop.Contracts.Billing;

namespace Shop.Reports.Api.Sales;

/// <summary>One captured payment as Reports keeps it: amounts only, no personal data.</summary>
public sealed record Sale(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    DateTimeOffset PaidAt
);

/// <summary>
/// The reports schema, created as the migrator role under the kernel's migration lock (Reports uses Dapper, so this
/// idempotent script is its whole migration story). Sales are tenant data under row-level security.
/// </summary>
public sealed class ReportsSchema(
    [FromKeyedServices(NpgsqlDataSourceKeys.Migration)] NpgsqlDataSource migrator,
    IMigrationLock migrationLock
) : IHostedService
{
    public const string ConnectionName = "reports";

    private const string Script = """
        CREATE TABLE IF NOT EXISTS sales (
            payment_id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            order_id uuid NOT NULL,
            amount numeric(19, 4) NOT NULL,
            currency char(3) NOT NULL,
            paid_at timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_sales_tenant_paid ON sales (tenant_id, paid_at);

        ALTER TABLE sales ENABLE ROW LEVEL SECURITY;
        ALTER TABLE sales FORCE ROW LEVEL SECURITY;
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'sales' AND policyname = 'tenant_isolation') THEN
                CREATE POLICY tenant_isolation ON sales
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            END IF;
        END $$;
        """;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var held = await migrationLock.AcquireAsync(
            ConnectionName,
            TimeSpan.FromMinutes(1),
            cancellationToken
        );
        await using var connection = await migrator.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition(Script, cancellationToken: cancellationToken)
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Records each captured payment for the publishing tenant (the consumer runs as Billing's caller, so the session binds
/// that tenant for row-level security). A redelivered receipt is the same payment: it is recorded once.
/// </summary>
public sealed class RecordSaleConsumer(
    IDbSessionFactory sessions,
    ILogger<RecordSaleConsumer> logger
) : ConsumerBase<EventEnvelope<ReceiptDue>>(logger)
{
    protected override async Task ConsumeAsync(
        EventEnvelope<ReceiptDue> envelope,
        CancellationToken ct
    )
    {
        var receipt = envelope.Data;
        await using var session = await sessions.OpenAsync(ct);
        await session.Connection.ExecuteAsync(
            session.Command(
                """
                INSERT INTO sales (payment_id, tenant_id, order_id, amount, currency, paid_at)
                VALUES (@paymentId, @tenant, @orderId, @amount, @currency, @paidAt)
                ON CONFLICT (payment_id) DO NOTHING
                """,
                new
                {
                    paymentId = receipt.PaymentId,
                    tenant = session.RequireTenantId().Value,
                    orderId = receipt.OrderId,
                    amount = receipt.Amount,
                    currency = receipt.Currency,
                    paidAt = receipt.OccurredOn,
                },
                ct
            )
        );
        await session.CommitAsync(ct);
    }
}

/// <summary>The caller tenant's sales, streamed row by row (an export never holds them all in memory).</summary>
public interface ISalesReader
{
    IAsyncEnumerable<Sale> ReadAsync(CancellationToken ct);
}

/// <summary>Reads the sales with Dapper, through a session bound to the caller's tenant (row-level security).</summary>
public sealed class SalesReader(IDbSessionFactory sessions) : ISalesReader
{
    public async IAsyncEnumerable<Sale> ReadAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
    )
    {
        await using var session = await sessions.OpenAsync(ct);
        var rows = session.Connection.QueryUnbufferedAsync<SaleRow>(
            "SELECT payment_id AS PaymentId, order_id AS OrderId, amount AS Amount, currency AS Currency, paid_at AS PaidAt FROM sales ORDER BY paid_at",
            transaction: session.Transaction
        );
        await foreach (var row in rows.WithCancellation(ct))
        {
            // Npgsql reads timestamptz as a UTC DateTime.
            yield return new Sale(
                row.PaymentId,
                row.OrderId,
                row.Amount,
                row.Currency,
                new DateTimeOffset(DateTime.SpecifyKind(row.PaidAt, DateTimeKind.Utc))
            );
        }
    }

    private sealed record SaleRow(
        Guid PaymentId,
        Guid OrderId,
        decimal Amount,
        string Currency,
        DateTime PaidAt
    );
}
