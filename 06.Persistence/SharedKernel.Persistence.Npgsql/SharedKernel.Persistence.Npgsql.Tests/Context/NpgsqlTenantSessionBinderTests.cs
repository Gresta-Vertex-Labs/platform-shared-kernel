using FluentAssertions;
using Npgsql;
using SharedKernel.Persistence.Npgsql.Context;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.Npgsql.Tests.Context;

/// <summary>
/// <see cref="NpgsqlTenantSessionBinder"/> against a real PostgreSQL Testcontainer: the
/// session setting is readable inside the transaction and resets once the transaction ends.
/// </summary>
public sealed class NpgsqlTenantSessionBinderTests : IAsyncLifetime
{
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
    public async Task BindAsync_SetsTheSessionSetting_ReadableWithinTheSameTransaction()
    {
        var binder = new NpgsqlTenantSessionBinder();
        var tenantId = Guid.NewGuid();

        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await binder.BindAsync(connection, transaction, tenantId, crossTenantActive: false);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT current_setting('app.tenant_id', true)";
        var value = (string?)await command.ExecuteScalarAsync();

        value.Should().Be(tenantId.ToString());

        await transaction.CommitAsync();
    }

    [Fact]
    public async Task BindAsync_SettingResets_AfterTheTransactionCommits()
    {
        var binder = new NpgsqlTenantSessionBinder();
        var tenantId = Guid.NewGuid();

        await using var connection = await _dataSource!.OpenConnectionAsync();

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await binder.BindAsync(connection, transaction, tenantId, crossTenantActive: false);
            await transaction.CommitAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_setting('app.tenant_id', true)";
        var value = (string?)await command.ExecuteScalarAsync();

        value.Should().BeEmpty("the transaction-local setting must reset once the transaction that bound it ends");
    }

    [Fact]
    public async Task BindAsync_SettingResets_AfterTheTransactionRollsBack()
    {
        var binder = new NpgsqlTenantSessionBinder();
        var tenantId = Guid.NewGuid();

        await using var connection = await _dataSource!.OpenConnectionAsync();

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await binder.BindAsync(connection, transaction, tenantId, crossTenantActive: false);
            await transaction.RollbackAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_setting('app.tenant_id', true)";
        var value = (string?)await command.ExecuteScalarAsync();

        value.Should().BeEmpty();
    }

    [Fact]
    public async Task BindAsync_NullTenantId_BindsEmptyString()
    {
        var binder = new NpgsqlTenantSessionBinder();

        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await binder.BindAsync(connection, transaction, tenantId: null, crossTenantActive: false);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT current_setting('app.tenant_id', true)";
        var value = (string?)await command.ExecuteScalarAsync();

        value.Should().BeEmpty();

        await transaction.CommitAsync();
    }

    [Fact]
    public async Task BindAsync_CrossTenantActive_SetsTheCrossTenantSetting()
    {
        var binder = new NpgsqlTenantSessionBinder();

        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await binder.BindAsync(connection, transaction, tenantId: null, crossTenantActive: true);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT current_setting('app.cross_tenant', true)";
        var value = (string?)await command.ExecuteScalarAsync();

        value.Should().Be("on");

        await transaction.CommitAsync();
    }

    [Fact]
    public async Task BindConnectionAsync_SetsTheSessionSetting_ReadableOutsideAnyTransaction()
    {
        var binder = new NpgsqlTenantSessionBinder();
        var tenantId = Guid.NewGuid();

        await using var connection = await _dataSource!.OpenConnectionAsync();

        await binder.BindConnectionAsync(connection, tenantId, crossTenantActive: false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_setting('app.tenant_id', true), current_setting('app.cross_tenant', true)";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        reader.GetString(0).Should().Be(tenantId.ToString());
        reader.GetString(1).Should().Be("off");
    }

    [Fact]
    public async Task BindConnectionAsync_SettingSurvives_AcrossAnInnerExplicitTransaction()
    {
        // A connection-scoped (is_local: false) binding is what an inner explicit transaction's own
        // COMMIT/ROLLBACK reverts back TO, not what it clears — proves the two binding shapes compose
        // correctly rather than one silently erasing the other.
        var binder = new NpgsqlTenantSessionBinder();
        var connectionTenantId = Guid.NewGuid();
        var transactionTenantId = Guid.NewGuid();

        await using var connection = await _dataSource!.OpenConnectionAsync();
        await binder.BindConnectionAsync(connection, connectionTenantId, crossTenantActive: false);

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await binder.BindAsync(connection, transaction, transactionTenantId, crossTenantActive: true);
            await transaction.CommitAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_setting('app.tenant_id', true), current_setting('app.cross_tenant', true)";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        reader.GetString(0).Should().Be(connectionTenantId.ToString());
        reader.GetString(1).Should().Be("off");
    }

    [Fact]
    public async Task ResetConnectionAsync_ClearsBothSettings()
    {
        var binder = new NpgsqlTenantSessionBinder();
        var tenantId = Guid.NewGuid();

        await using var connection = await _dataSource!.OpenConnectionAsync();
        await binder.BindConnectionAsync(connection, tenantId, crossTenantActive: true);

        await binder.ResetConnectionAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_setting('app.tenant_id', true), current_setting('app.cross_tenant', true)";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        reader.GetString(0).Should().BeEmpty();
        reader.GetString(1).Should().Be("off");
    }
}
