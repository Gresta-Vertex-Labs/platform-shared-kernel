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
            await scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
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
            await scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
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
            await scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
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
            await scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
        }

        await using var testScope = sp.CreateAsyncScope();
        var context = testScope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();

        var act = async () => await context.Set<AuditRecord>().ExecuteDeleteAsync();

        await act.Should().ThrowAsync<AuditRecordImmutableException>();
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
            written = await scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
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
            written = await scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
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
            await scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>().RecordAsync(FailedEntry("Order", "order-1"));
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
        var act = async () => await testScope.ServiceProvider.GetRequiredService<IAuditTrailWriter>()
            .RecordAsync(FailedEntry("Order", "order-1"));

        await act.Should().NotThrowAsync("the trigger only blocks UPDATE/DELETE/TRUNCATE — normal appends via IAuditTrailWriter's INSERT must be unaffected");
    }
}
