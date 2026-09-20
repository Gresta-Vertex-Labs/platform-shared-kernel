using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using SharedKernel.Persistence.PostgreSQL.Migrations;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Migrations;

/// <summary>
/// <see cref="AuditImmutabilityMigrationBuilderExtensions.CreateImmutabilityTrigger"/>
/// against a real PostgreSQL Testcontainer: an UPDATE, a DELETE, and a TRUNCATE against a protected
/// table are all rejected once the trigger is applied, while INSERT remains unaffected.
/// </summary>
public sealed class AuditImmutabilityMigrationBuilderExtensionsIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture = new();
    private NpgsqlDataSource? _dataSource;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);

        await using var connection = await _dataSource.OpenConnectionAsync();

        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                DROP TABLE IF EXISTS immutable_audit_row;
                CREATE TABLE immutable_audit_row (id SERIAL PRIMARY KEY, payload TEXT NOT NULL);
                """;
            await schema.ExecuteNonQueryAsync();
        }

        var migrationBuilder = new MigrationBuilder(activeProvider: "Npgsql");
        migrationBuilder.CreateImmutabilityTrigger("immutable_audit_row");

        foreach (var operation in migrationBuilder.Operations.OfType<SqlOperation>())
        {
            await using var ddl = connection.CreateCommand();
            ddl.CommandText = operation.Sql;
            await ddl.ExecuteNonQueryAsync();
        }

        await using var seed = connection.CreateCommand();
        seed.CommandText = "INSERT INTO immutable_audit_row (payload) VALUES ('original')";
        await seed.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();

        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task Update_AgainstProtectedTable_IsRejected()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE immutable_audit_row SET payload = 'tampered' WHERE id = 1";

        var act = async () => await command.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task Delete_AgainstProtectedTable_IsRejected()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM immutable_audit_row WHERE id = 1";

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task Truncate_AgainstProtectedTable_IsRejected()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "TRUNCATE TABLE immutable_audit_row";

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task Insert_AgainstProtectedTable_StillSucceeds()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO immutable_audit_row (payload) VALUES ('second-row')";

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DropImmutabilityTrigger_RemovesTheProtection()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();

        var migrationBuilder = new MigrationBuilder(activeProvider: "Npgsql");
        migrationBuilder.DropImmutabilityTrigger("immutable_audit_row");

        foreach (var operation in migrationBuilder.Operations.OfType<SqlOperation>())
        {
            await using var ddl = connection.CreateCommand();
            ddl.CommandText = operation.Sql;
            await ddl.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE immutable_audit_row SET payload = 'now-allowed' WHERE id = 1";

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().NotThrowAsync();
    }
}
