using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.EfCore.Context;
using SharedKernel.Idempotency.EfCore.Tests.Concurrency;
using SharedKernel.Idempotency.EfCore.Tests.Support;
using SharedKernel.Persistence.Testing;
using SharedKernel.Testing.Containers;
using Xunit;

namespace SharedKernel.Idempotency.EfCore.Tests.Extensions;

/// <summary>
/// The shipped migration and the <c>MigrateOnStartup</c> opt-in against a real PostgreSQL under the canonical role
/// split (<c>app_migrator</c> owns the schema, <c>app_runtime</c> cannot create anything).
/// </summary>
/// <remarks>REQUIRES A DOCKER DAEMON.</remarks>
[Collection("PostgreSqlContainer")]
public sealed class EfCoreIdempotencyMigrationPostgresTests(PostgreSqlContainerFixture fixture)
{
    private const string HistoryTable = "__ef_migrations_history_idempotency";

    /// <summary>The documented manual-fallback DDL (README); the same columns the model declares.</summary>
    private const string HandMadeTable =
        """
        CREATE TABLE idempotency_keys (
            tenant_scope character varying(36) NOT NULL,
            purpose character varying(16) NOT NULL,
            key character varying(512) NOT NULL,
            fingerprint character varying(128) NOT NULL,
            status character varying(20) NOT NULL,
            reservation_token uuid NOT NULL,
            reserved_at_utc timestamp with time zone NOT NULL,
            expires_at_utc timestamp with time zone NOT NULL,
            response text,
            CONSTRAINT pk_idempotency_keys PRIMARY KEY (tenant_scope, purpose, key)
        );
        CREATE INDEX ix_idempotency_keys_expires_at_utc ON idempotency_keys (expires_at_utc);
        """;

    private const string Rows =
        """
        INSERT INTO idempotency_keys VALUES
          ('no-tenant', 'Request', 'k-completed', 'fp', 'Completed', gen_random_uuid(), now(), now() + interval '1 day', '{"replayed":true}'),
          ('no-tenant', 'Request', 'k-inflight', 'fp', 'InProgress', gen_random_uuid(), now(), now() + interval '1 day', NULL),
          ('no-tenant', 'Request', 'k-expired', 'fp', 'InProgress', gen_random_uuid(), now() - interval '2 hours', now() - interval '1 hour', NULL);
        """;

    private static void OptIn(SharedKernel.Idempotency.EfCore.Options.EfCoreIdempotencyOptions o) => o.MigrateOnStartup = true;

    // ---- T-02: the shipped table equals the model, and the store works on it ----

    [Fact]
    public async Task TheShippedMigration_CreatesExactlyTheTableTheModelDeclares_AndAllFourStatusesWorkOnIt()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await using var reference = await fixture.Server.CreateDatabaseAsync();
        await using var host = MigrationHost.Create(db, OptIn);

        await host.StartAsync();

        // The model's own DDL (what EnsureCreated would run), created as the migrator in a second database.
        await using (var scope = host.Provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdempotencyDbContext>();
            await reference.CreateSchemaAsync(context);
            Assert.False(context.Database.HasPendingModelChanges(), "the migration snapshot equals the model");
        }

        var migrated = await Sql.CatalogOfAsync(db.AdminConnectionString);
        Assert.NotNull(migrated);
        Assert.Equal(await Sql.CatalogOfAsync(reference.AdminConnectionString), migrated);

        var started = await host.BeginAsync("k-1");
        Assert.Equal(IdempotencyReservationStatus.Started, started.Status);
        Assert.Equal(IdempotencyReservationStatus.InProgress, (await host.BeginAsync("k-1")).Status);
        Assert.Equal(IdempotencyReservationStatus.FingerprintMismatch, (await host.BeginAsync("k-1", "other")).Status);
        Assert.True(await host.CompleteAsync("k-1", started.Token!, "{\"a\":1}"));
        var replay = await host.BeginAsync("k-1");
        Assert.Equal(IdempotencyReservationStatus.Completed, replay.Status);
        Assert.Equal("{\"a\":1}", replay.StoredResponse);
    }

    // ---- T-03: the real role split ----

    [Fact]
    public async Task UnderTheRoleSplit_TheMigratorOwnsTheSchema_AndTheStoreRunsAsTheRuntimeRole()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await using var host = MigrationHost.Create(db, OptIn);

        await host.StartAsync();

        Assert.Equal(PostgresTestRoles.Migrator, await Sql.ScalarAsync<string>(db.AdminConnectionString, "SELECT tableowner FROM pg_tables WHERE tablename = 'idempotency_keys'"));
        Assert.Equal(PostgresTestRoles.Migrator, await Sql.ScalarAsync<string>(db.AdminConnectionString, $"SELECT tableowner FROM pg_tables WHERE tablename = '{HistoryTable}'"));
        Assert.Equal(1, await Sql.HistoryCountAsync(db.AdminConnectionString, HistoryTable));
        Assert.EndsWith("_InitialIdempotencySchema", await Sql.ScalarAsync<string>(db.AdminConnectionString, $"SELECT migration_id FROM \"{HistoryTable}\""));
        Assert.False(await Sql.ExistsAsync(db.AdminConnectionString, "__EFMigrationsHistory"));

        // No DDL on the runtime connection: every statement that created something ran as the migrator.
        var ddlUsers = host.Commands.UsersWhere("CREATE ");
        Assert.NotEmpty(ddlUsers);
        Assert.All(ddlUsers, user => Assert.Equal(PostgresTestRoles.Migrator, user));

        // The store as app_runtime: Started, Complete, replay, release rules, cleanup DELETE.
        var started = await host.BeginAsync("k-split");
        Assert.Equal(IdempotencyReservationStatus.Started, started.Status);
        Assert.True(await host.CompleteAsync("k-split", started.Token!, "body"));
        var replay = await host.BeginAsync("k-split");
        Assert.Equal(IdempotencyReservationStatus.Completed, replay.Status);
        Assert.Equal("body", replay.StoredResponse);
        Assert.False(await host.ReleaseAsync("k-split", started.Token!), "a completed entry is never released");

        var other = await host.BeginAsync("k-release");
        Assert.True(await host.ReleaseAsync("k-release", other.Token!));

        Assert.True(await Sql.ExecuteAsync(db.RuntimeConnectionString, "DELETE FROM idempotency_keys WHERE expires_at_utc < now()") >= 0);

        var denied = await Assert.ThrowsAsync<PostgresException>(
            () => Sql.ExecuteAsync(db.RuntimeConnectionString, "CREATE TABLE runtime_made (id int)"));
        Assert.Equal("42501", denied.SqlState);
    }

    // ---- T-04: a table that exists already, in three shapes ----

    public enum ExistingShape
    {
        HandMadeByMigrator,
        OwnedByRuntimeRole,
        ConsumerMigrationOnDefaultHistory,
    }

    [Theory]
    [InlineData(ExistingShape.HandMadeByMigrator)]
    [InlineData(ExistingShape.OwnedByRuntimeRole)]
    [InlineData(ExistingShape.ConsumerMigrationOnDefaultHistory)]
    public async Task OptingInOnAnExistingTable_ChangesNothing_AndOnlyAddsTheHistoryRow(ExistingShape shape)
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await db.ExecuteAsMigratorAsync(HandMadeTable);
        switch (shape)
        {
            case ExistingShape.OwnedByRuntimeRole:
                await db.ExecuteAsAdminAsync($"ALTER TABLE idempotency_keys OWNER TO {PostgresTestRoles.Runtime};");
                break;
            case ExistingShape.ConsumerMigrationOnDefaultHistory:
                await db.ExecuteAsMigratorAsync(
                    """
                    CREATE TABLE "__EFMigrationsHistory" ("MigrationId" character varying(150) NOT NULL, "ProductVersion" character varying(32) NOT NULL, CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId"));
                    INSERT INTO "__EFMigrationsHistory" VALUES ('20250101000000_ConsumerIdempotency', '10.0.0');
                    """);
                break;
        }

        await db.ExecuteAsAdminAsync(Rows);
        var catalogBefore = await Sql.CatalogOfAsync(db.AdminConnectionString);
        var rowsBefore = await Sql.RowsDigestAsync(db.AdminConnectionString);
        var defaultHistoryBefore = shape == ExistingShape.ConsumerMigrationOnDefaultHistory
            ? await Sql.ScalarAsync<string>(db.AdminConnectionString, "SELECT string_agg(\"MigrationId\", ',') FROM \"__EFMigrationsHistory\"")
            : null;

        await using (var host = MigrationHost.Create(db, OptIn))
        {
            await host.StartAsync();

            Assert.Equal(catalogBefore, await Sql.CatalogOfAsync(db.AdminConnectionString));
            Assert.Equal(rowsBefore, await Sql.RowsDigestAsync(db.AdminConnectionString));
            Assert.Equal(1, await Sql.HistoryCountAsync(db.AdminConnectionString, HistoryTable));
            Assert.DoesNotContain(host.Commands.All, c => c.Sql.Contains("ALTER TABLE", StringComparison.Ordinal) && c.Sql.Contains("idempotency_keys", StringComparison.Ordinal));

            if (shape == ExistingShape.ConsumerMigrationOnDefaultHistory)
            {
                Assert.Equal(defaultHistoryBefore, await Sql.ScalarAsync<string>(db.AdminConnectionString, "SELECT string_agg(\"MigrationId\", ',') FROM \"__EFMigrationsHistory\""));
            }

            var completed = await host.BeginAsync("k-completed");
            Assert.Equal(IdempotencyReservationStatus.Completed, completed.Status);
            Assert.Equal("{\"replayed\":true}", completed.StoredResponse);
            Assert.Equal(IdempotencyReservationStatus.InProgress, (await host.BeginAsync("k-inflight")).Status);
        }

        // A second start is a no-op.
        await using (var again = MigrationHost.Create(db, OptIn))
        {
            var digest = await Sql.RowsDigestAsync(db.AdminConnectionString);
            await again.StartAsync();

            Assert.Equal(1, await Sql.HistoryCountAsync(db.AdminConnectionString, HistoryTable));
            Assert.Equal(catalogBefore, await Sql.CatalogOfAsync(db.AdminConnectionString));
            Assert.Equal(digest, await Sql.RowsDigestAsync(db.AdminConnectionString));
        }
    }

    // ---- T-05: the default is off ----

    [Fact]
    public async Task WithoutTheOptIn_NothingIsCreated_AndTheFirstStoreCallFailsOnTheMissingTable()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await using var host = MigrationHost.Create(db);

        await host.StartAsync();

        Assert.False(await Sql.ExistsAsync(db.AdminConnectionString, "idempotency_keys"));
        Assert.False(await Sql.ExistsAsync(db.AdminConnectionString, HistoryTable));
        Assert.False(await Sql.ExistsAsync(db.AdminConnectionString, "__EFMigrationsHistory"));

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => host.BeginAsync("k"));
        var postgres = failure as PostgresException ?? failure.InnerException as PostgresException;
        Assert.Equal("42P01", postgres?.SqlState);
        Assert.Empty(host.Commands.UsersWhere("CREATE "));
    }

    [Fact]
    public async Task OptedInWithoutAMigrationConnection_UsesTheContextsOwnConnection_AndCreatesTheSchema()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        // A single-role host: the store's own connection is the owner role, there is no MigrationConnectionString.
        await using var host = MigrationHost.Create(db, OptIn, withMigrator: false, storeConnectionString: db.MigratorConnectionString);

        await host.StartAsync();

        Assert.True(await Sql.ExistsAsync(db.AdminConnectionString, "idempotency_keys"));
        Assert.Equal(1, await Sql.HistoryCountAsync(db.AdminConnectionString, HistoryTable));
        Assert.Equal(IdempotencyReservationStatus.Started, (await host.BeginAsync("k")).Status);
    }
}
