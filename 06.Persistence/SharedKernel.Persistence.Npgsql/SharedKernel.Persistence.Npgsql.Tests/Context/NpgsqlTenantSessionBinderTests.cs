using FluentAssertions;
using Npgsql;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.Npgsql.Context;
using SharedKernel.Persistence.Npgsql.RowLevelSecurity;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.Npgsql.Tests.Context;

/// <summary>
/// <see cref="NpgsqlTenantSessionBinder"/> and <see cref="TenantSessionSql.BindStatement"/> against a real
/// PostgreSQL: the tenant is visible inside the binding transaction only, and a bind statement prefixed to
/// a command outside a transaction covers exactly that command without changing its results.
/// </summary>
public sealed class NpgsqlTenantSessionBinderTests : IAsyncLifetime
{
    private const string ReadSettingSql = "SELECT current_setting('app.tenant_id', true)";

    private readonly PostgreSqlContainerFixture _fixture = new();
    private NpgsqlDataSource? _dataSource;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();

        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task BindAsync_SetsTheTenant_ReadableWithinTheSameTransaction()
    {
        var tenantId = new TenantId(Guid.NewGuid());

        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await new NpgsqlTenantSessionBinder().BindAsync(connection, transaction, tenantId);

        (await ReadSettingAsync(connection, transaction)).Should().Be(tenantId.ToString());
        await transaction.CommitAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BindAsync_SettingIsGone_AfterTheTransactionEnds(bool commit)
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await new NpgsqlTenantSessionBinder().BindAsync(connection, transaction, new TenantId(Guid.NewGuid()));
            if (commit)
                await transaction.CommitAsync();
            else
                await transaction.RollbackAsync();
        }

        (await ReadSettingAsync(connection, null)).Should().BeNullOrEmpty(
            "a transaction-local setting must never outlive its transaction");
    }

    [Fact]
    public async Task BindAsync_NullTenant_BindsTheEmptyString()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await new NpgsqlTenantSessionBinder().BindAsync(connection, transaction, tenantId: null);

        (await ReadSettingAsync(connection, transaction)).Should().BeEmpty();
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task BindStatement_PrefixedOutsideATransaction_BindsForThatCommandOnly_AndKeepsItsResult()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        await using var connection = await _dataSource!.OpenConnectionAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = TenantSessionSql.BindStatement(tenantId) + ReadSettingSql;
            (await command.ExecuteScalarAsync()).Should().Be(
                tenantId.ToString(), "the DO block returns no result set, so the query's value comes first");
        }

        (await ReadSettingAsync(connection, null)).Should().BeNullOrEmpty(
            "the statements of one command run in one implicit transaction, which ended with the command");
    }

    [Fact]
    public async Task BindStatement_PrefixedToAReader_ReturnsOnlyTheQuerysRows()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = TenantSessionSql.BindStatement(new TenantId(Guid.NewGuid())) + "SELECT g FROM generate_series(1, 3) g";

        var values = new List<int>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                values.Add(reader.GetInt32(0));

            (await reader.NextResultAsync()).Should().BeFalse();
        }

        values.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task BindStatement_PrefixedToANonQuery_KeepsTheRowsAffected()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TEMP TABLE binder_rows (id int)";
            await create.ExecuteNonQueryAsync();
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = TenantSessionSql.BindStatement(new TenantId(Guid.NewGuid())) + "INSERT INTO binder_rows SELECT generate_series(1, 4)";

        (await insert.ExecuteNonQueryAsync()).Should().Be(4);
    }

    [Fact]
    public void BindStatement_InlinesOnlyTheGuid()
    {
        var tenantId = TenantId.Parse("0b9a5c35-58e1-4f5c-a0d8-000000000001");

        TenantSessionSql.BindStatement(tenantId).Should().Be(
            "DO $sk_rls$BEGIN PERFORM set_config('app.tenant_id', '0b9a5c35-58e1-4f5c-a0d8-000000000001', true); END$sk_rls$;");
        TenantSessionSql.BindStatement(null).Should().Contain("'app.tenant_id', '', true");
    }

    [Fact]
    public void PolicyPredicate_IsTheSingleTenantComparison()
    {
        TenantSessionSql.PolicyPredicate("\"tenant_id\"").Should().Be(
            "\"tenant_id\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid");
    }

    private static async Task<string?> ReadSettingAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ReadSettingSql;
        return (string?)await command.ExecuteScalarAsync();
    }
}
