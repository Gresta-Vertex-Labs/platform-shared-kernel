using SharedKernel.Application.Auditing;
using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.EfCore.Auditing.Tests.TestFixtures;
using SharedKernel.Persistence.PostgreSQL.Migrations;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>
/// Defense-in-depth proof for the audit table's append-only guarantee, at all three layers, against
/// real PostgreSQL: the EF Core tracked-entity guard (<c>AuditRecordImmutabilityInterceptor</c>), the
/// EF Core <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>/raw-SQL guard (<c>AuditRecordMutationGuardInterceptor</c>),
/// and — the mandatory production layer neither of those two substitutes for — the PostgreSQL
/// <c>BEFORE UPDATE/DELETE/TRUNCATE</c> trigger (<c>AuditImmutabilityMigrationBuilderExtensions.CreateImmutabilityTrigger</c>),
/// applied here to the REAL <c>audit_records</c> table.
/// </summary>
[Collection("AuditPostgres")]
public sealed class AuditChainMutationGuardPostgresTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public AuditChainMutationGuardPostgresTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = database }.ConnectionString;

    private static AuditEntry FailedEntry(string resourceType, string resourceId) => new()
    {
        Action = "Tested",
        ResourceType = resourceType,
        ResourceId = resourceId,
        Outcome = AuditOutcome.Failed,
        ErrorCode = "test.failure",
    };

    private static async Task ApplyImmutabilityTriggerAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var migrationBuilder = new MigrationBuilder(activeProvider: "Npgsql");
        migrationBuilder.CreateImmutabilityTrigger(AuditSchema.TableName);

        foreach (var operation in migrationBuilder.Operations.OfType<SqlOperation>())
        {
            await using var ddl = connection.CreateCommand();
            ddl.CommandText = operation.Sql;
            await ddl.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Interceptor_TrackedUpdate_ThrowsAuditRecordImmutableException()
    {
        var connectionString = ConnectionString("sk_audit_guard_tracked_update");
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext());

        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
        }

        await using var testScope = sp.CreateAsyncScope();
        var context = testScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();
        var tracked = await context.Set<AuditRecord>().FirstAsync();
        context.Entry(tracked).Property(x => x.Action).CurrentValue = "Tampered";
        context.Entry(tracked).State = EntityState.Modified;

        var act = async () => await context.SaveChangesAsync();

        (await act.Should().ThrowAsync<AuditRecordImmutableException>())
            .Which.RecordId.Should().Be(tracked.Id);
    }

    [Fact]
    public async Task Interceptor_TrackedDelete_ThrowsAuditRecordImmutableException()
    {
        var connectionString = ConnectionString("sk_audit_guard_tracked_delete");
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext());

        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
        }

        await using var testScope = sp.CreateAsyncScope();
        var context = testScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();
        var tracked = await context.Set<AuditRecord>().FirstAsync();
        context.Entry(tracked).State = EntityState.Deleted;

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<AuditRecordImmutableException>();
    }

    [Fact]
    public async Task MutationGuardInterceptor_ExecuteUpdate_IsRejected()
    {
        var connectionString = ConnectionString("sk_audit_guard_executeupdate");
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext());

        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
        }

        await using var testScope = sp.CreateAsyncScope();
        var context = testScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();

        // ExecuteUpdateAsync never populates the ChangeTracker — AuditRecordImmutabilityInterceptor
        // (a SaveChanges interceptor) cannot see it at all; only AuditRecordMutationGuardInterceptor
        // (a DbCommandInterceptor, inspecting the generated SQL text) catches this path.
        var act = async () => await context.Set<AuditRecord>()
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Action, "Tampered"));

        await act.Should().ThrowAsync<AuditRecordImmutableException>();
    }

    [Fact]
    public async Task MutationGuardInterceptor_ExecuteDelete_IsRejected()
    {
        var connectionString = ConnectionString("sk_audit_guard_executedelete");
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext());

        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
        }

        await using var testScope = sp.CreateAsyncScope();
        var context = testScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();

        var act = async () => await context.Set<AuditRecord>().ExecuteDeleteAsync();

        await act.Should().ThrowAsync<AuditRecordImmutableException>();
    }

    [Fact]
    public async Task MutationGuardInterceptor_TruncateViaRawSqlThroughDbContext_IsRejected()
    {
        // Medium regression: the interceptor's regex used to match only UPDATE/DELETE FROM — never
        // TRUNCATE. A raw TRUNCATE reached THROUGH this DbContext is invisible to
        // AuditRecordImmutabilityInterceptor (change-tracker based); only the widened
        // AuditRecordMutationGuardInterceptor regex catches it at the APPLICATION layer — distinct
        // from DatabaseTrigger_TruncateRealAuditTable_IsRejected above, which proves the separate,
        // mandatory DATABASE-layer trigger against a raw connection that bypasses the app entirely.
        var connectionString = ConnectionString("sk_audit_guard_truncate_via_context");
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext());

        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
        }

        await using var testScope = sp.CreateAsyncScope();
        var context = testScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();

        var act = async () => await context.Database.ExecuteSqlRawAsync($"""TRUNCATE TABLE "{AuditSchema.TableName}" """);

        await act.Should().ThrowAsync<AuditRecordImmutableException>();
    }

    [Fact]
    public async Task MutationGuardInterceptor_SchemaQualifiedDeleteThroughDbContext_IsRejected()
    {
        // Medium regression: the un-widened regex required the bare table name immediately after
        // "DELETE FROM" — a schema-qualified reference ("public"."audit_records") slipped past it.
        var connectionString = ConnectionString("sk_audit_guard_schema_qualified_delete");
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext());

        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
        }

        await using var testScope = sp.CreateAsyncScope();
        var context = testScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();

        var act = async () => await context.Database.ExecuteSqlRawAsync($"""DELETE FROM "public"."{AuditSchema.TableName}" """);

        await act.Should().ThrowAsync<AuditRecordImmutableException>();
    }

    [Fact]
    public async Task MutationGuardInterceptor_UpdateOnlyThroughDbContext_IsRejected()
    {
        // Medium regression: Postgres's UPDATE ONLY table_name (table-inheritance syntax) — the
        // un-widened regex required the table name immediately after "UPDATE", with no room for ONLY.
        var connectionString = ConnectionString("sk_audit_guard_update_only");
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext());

        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
        }

        await using var testScope = sp.CreateAsyncScope();
        var context = testScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();

        var act = async () => await context.Database.ExecuteSqlRawAsync($"""UPDATE ONLY "{AuditSchema.TableName}" SET "{AuditSchema.Action}" = 'Tampered'""");

        await act.Should().ThrowAsync<AuditRecordImmutableException>();
    }

    [Fact]
    public async Task MutationGuardInterceptor_AlterTableDisableTriggerThroughDbContext_IsRejected()
    {
        // Medium regression: the un-widened regex covered only UPDATE/DELETE FROM — an attacker (or a
        // careless migration) disabling the immutability TRIGGER itself via raw SQL reached through
        // this DbContext was not caught at the application layer at all.
        var connectionString = ConnectionString("sk_audit_guard_alter_disable_trigger");
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext());

        await using (var scope = sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();

        await using var testScope = sp.CreateAsyncScope();
        var context = testScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();

        var act = async () => await context.Database.ExecuteSqlRawAsync($"""ALTER TABLE "{AuditSchema.TableName}" DISABLE TRIGGER ALL""");

        await act.Should().ThrowAsync<AuditRecordImmutableException>();
    }

    [Fact]
    public async Task MutationGuardInterceptor_DropTableThroughDbContext_IsRejected()
    {
        // Medium regression: DROP TABLE was not covered by the un-widened regex at all.
        var connectionString = ConnectionString("sk_audit_guard_drop_table");
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext());

        await using (var scope = sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();

        await using var testScope = sp.CreateAsyncScope();
        var context = testScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();

        var act = async () => await context.Database.ExecuteSqlRawAsync($"""DROP TABLE "{AuditSchema.TableName}" """);

        await act.Should().ThrowAsync<AuditRecordImmutableException>();
    }

    [Fact]
    public async Task RecordAsync_FailedOutcome_UnderlyingWriteItselfFails_ThrowsAndInsertsNothing()
    {
        // Medium regression: AuditingLog.FailureAuditWriteFailed (EventId 6405) was defined but never
        // called — this exercises the exact path that now calls it: a Failed-outcome write (its own,
        // independent connection/transaction) that itself fails at the DB layer. A resourceType longer
        // than the real varchar(200) column forces a genuine Postgres 22001 ("value too long") error —
        // a real data-layer failure, not a simulated one.
        var connectionString = ConnectionString("sk_audit_guard_failure_write_itself_fails");
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext());

        await using (var scope = sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();

        await using var testScope = sp.CreateAsyncScope();
        var writer = testScope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>();

        var oversizedResourceType = new string('x', 201);

        var act = async () => await writer.RecordAsync(new AuditEntry
        {
            Action = "Tested",
            ResourceType = oversizedResourceType,
            ResourceId = "order-1",
            Outcome = AuditOutcome.Failed,
            ErrorCode = "test.failure",
        });

        await act.Should().ThrowAsync<PostgresException>();

        await using var verifyScope = sp.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();
        (await verifyContext.Set<AuditRecord>().CountAsync()).Should().Be(0, "the failed write must roll back completely, not leave a partial row");
    }

    [Fact]
    public async Task DatabaseTrigger_RawUpdateAgainstRealAuditTable_IsRejected()
    {
        var connectionString = ConnectionString("sk_audit_guard_trigger_update");
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext());

        AuditRecord written;
        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();
            written = await scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
        }

        await ApplyImmutabilityTriggerAsync(connectionString);

        await using var raw = new NpgsqlConnection(connectionString);
        await raw.OpenAsync();
        await using var command = raw.CreateCommand();
        command.CommandText = $"""UPDATE "{AuditSchema.TableName}" SET "{AuditSchema.Action}" = 'Tampered' WHERE "{AuditSchema.Id}" = @id""";
        command.Parameters.AddWithValue("id", written.Id);

        var act = async () => await command.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task DatabaseTrigger_RawDeleteAgainstRealAuditTable_IsRejected()
    {
        var connectionString = ConnectionString("sk_audit_guard_trigger_delete");
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext());

        AuditRecord written;
        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();
            written = await scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
        }

        await ApplyImmutabilityTriggerAsync(connectionString);

        await using var raw = new NpgsqlConnection(connectionString);
        await raw.OpenAsync();
        await using var command = raw.CreateCommand();
        command.CommandText = $"""DELETE FROM "{AuditSchema.TableName}" WHERE "{AuditSchema.Id}" = @id""";
        command.Parameters.AddWithValue("id", written.Id);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task DatabaseTrigger_TruncateRealAuditTable_IsRejected()
    {
        var connectionString = ConnectionString("sk_audit_guard_trigger_truncate");
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext());

        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
        }

        await ApplyImmutabilityTriggerAsync(connectionString);

        await using var raw = new NpgsqlConnection(connectionString);
        await raw.OpenAsync();
        await using var command = raw.CreateCommand();
        command.CommandText = $"""TRUNCATE TABLE "{AuditSchema.TableName}" """;

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task DatabaseTrigger_InsertAgainstRealAuditTable_StillSucceeds()
    {
        var connectionString = ConnectionString("sk_audit_guard_trigger_insert");
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext());

        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>().Database.EnsureCreatedAsync();
        }

        await ApplyImmutabilityTriggerAsync(connectionString);

        await using var testScope = sp.CreateAsyncScope();
        var act = async () => await testScope.ServiceProvider.GetRequiredService<EfAuditTrailWriter>()
            .RecordAsync(FailedEntry("Order", "order-1"));

        await act.Should().NotThrowAsync("the trigger only blocks UPDATE/DELETE/TRUNCATE — normal appends via IAuditTrailWriter's INSERT must be unaffected");
    }
}
