using System.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Npgsql.Connections;
using SharedKernel.Testing.Containers;

using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.Npgsql.Tests.Connections;

/// <summary>
/// <see cref="NpgsqlConnectionFactory"/>/<see cref="NpgsqlPersistenceExtensions"/>
/// integration tests against a real PostgreSQL Testcontainer (this package is Npgsql-only, no EF
/// Core provider, so a genuine open connection is the only meaningful proof).
/// </summary>
public sealed class NpgsqlConnectionFactoryTests : IAsyncLifetime
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
    public async Task CreateConnectionAsync_ReturnsOpenConnection()
    {
        var factory = new NpgsqlConnectionFactory(_dataSource!);

        await using var connection = await factory.CreateConnectionAsync();

        connection.State.Should().Be(ConnectionState.Open);
        connection.Should().BeOfType<NpgsqlConnection>();
    }

    [Fact]
    public async Task CreateConnectionAsync_CanExecuteQuery()
    {
        var factory = new NpgsqlConnectionFactory(_dataSource!);

        await using var connection = await factory.CreateConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var result = await command.ExecuteScalarAsync();

        result.Should().Be(1);
    }

    [Fact]
    public async Task CreateConnectionAsync_EachCall_ReturnsIndependentConnection()
    {
        var factory = new NpgsqlConnectionFactory(_dataSource!);

        await using var connection1 = await factory.CreateConnectionAsync();
        await using var connection2 = await factory.CreateConnectionAsync();

        ReferenceEquals(connection1, connection2).Should().BeFalse(
            "each call must open a fresh pooled connection, never reuse the same instance");
    }

    [Fact]
    public void AddSharedKernelNpgsql_RegistersDataSourceAndConnectionFactory()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelNpgsql(TestNpgsqlConfiguration.Create(_fixture.ConnectionString));

        var provider = services.BuildServiceProvider();

        var dataSource = provider.GetService<NpgsqlDataSource>();
        dataSource.Should().NotBeNull("AddSharedKernelNpgsql registers a singleton NpgsqlDataSource");

        using var scope = provider.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetService<IDbConnectionFactory>();
        connectionFactory.Should().NotBeNull();
        connectionFactory.Should().BeOfType<NpgsqlConnectionFactory>();
    }

    [Fact]
    public async Task AddSharedKernelNpgsql_RegisteredConnectionFactory_OpensGenuineConnection()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelNpgsql(TestNpgsqlConfiguration.Create(_fixture.ConnectionString));
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        await using var connection = await connectionFactory.CreateConnectionAsync();

        connection.State.Should().Be(ConnectionState.Open);
    }
}
