using Dapper;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.Npgsql.Connections;

namespace Shop.Inventory.Api.Stock;

/// <summary>
/// Creates the inventory schema before the host serves traffic: as the migrator role, under the kernel's migration
/// lock, so replicas starting together run it one at a time. Idempotent. A Dapper-only service has no EF Core
/// migrations, so this is the whole migration story: one ordered script, safe to run again.
/// </summary>
public sealed class InventorySchema(
    [FromKeyedServices(NpgsqlDataSourceKeys.Migration)] NpgsqlDataSource migrator,
    IMigrationLock migrationLock
) : IHostedService
{
    public const string ConnectionName = "inventory";

    // The policy predicate is the one SharedKernel.Persistence.Npgsql binds and checks at startup.
    private const string Script = """
        CREATE TABLE IF NOT EXISTS stock_items (
            tenant_id uuid NOT NULL,
            sku varchar(32) NOT NULL,
            on_hand integer NOT NULL CHECK (on_hand >= 0),
            reserved integer NOT NULL DEFAULT 0 CHECK (reserved >= 0 AND reserved <= on_hand),
            PRIMARY KEY (tenant_id, sku)
        );

        CREATE TABLE IF NOT EXISTS reservations (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            order_id uuid NOT NULL,
            sku varchar(32) NOT NULL,
            quantity integer NOT NULL CHECK (quantity > 0),
            released boolean NOT NULL DEFAULT false
        );
        CREATE INDEX IF NOT EXISTS ix_reservations_tenant_order ON reservations (tenant_id, order_id);

        -- Not tenant data: one row per job execution, so a duplicate execution is visible, not hidden by a constraint.
        CREATE TABLE IF NOT EXISTS job_runs (
            id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            job varchar(100) NOT NULL,
            fire_time timestamptz NOT NULL,
            replica varchar(200) NOT NULL,
            items integer NOT NULL
        );

        DO $$
        DECLARE t text;
        BEGIN
            FOREACH t IN ARRAY ARRAY['stock_items', 'reservations'] LOOP
                EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', t);
                EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', t);
                IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = t AND policyname = 'tenant_isolation') THEN
                    EXECUTE format(
                        'CREATE POLICY tenant_isolation ON %I USING (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid) WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid)',
                        t);
                END IF;
            END LOOP;
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
