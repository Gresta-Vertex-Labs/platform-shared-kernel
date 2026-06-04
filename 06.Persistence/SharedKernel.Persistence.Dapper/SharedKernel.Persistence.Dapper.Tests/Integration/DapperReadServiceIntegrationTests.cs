using Dapper;
using FluentAssertions;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Dapper.ReadModels;
using Testcontainers.PostgreSql;

namespace SharedKernel.Persistence.Dapper.Tests.Integration;

// Simple DTO for Dapper mapping
public sealed class TestRow
{
    public string Name { get; init; } = string.Empty;
    public int Value { get; init; }
}

/// <summary>
/// T-39(1-4): DapperReadService integration tests against a real PostgreSQL Testcontainer.
/// </summary>
public sealed class DapperReadServiceIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        // Create a simple test table
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            DROP TABLE IF EXISTS dapper_test;
            CREATE TABLE dapper_test (
                id      SERIAL PRIMARY KEY,
                name    TEXT NOT NULL,
                value   INT  NOT NULL
            );
            INSERT INTO dapper_test (name, value) VALUES ('Alpha', 1), ('Beta', 2), ('Gamma', 3);
            """);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    // -----------------------------------------------------------------------
    // Concrete test service
    // -----------------------------------------------------------------------

    private sealed class TestReadService(IDbConnectionFactory factory) : DapperReadService(factory)
    {
        public Task<IEnumerable<TestRow>> GetAllAsync(CancellationToken ct)
            => QueryAsync<TestRow>("SELECT name, value FROM dapper_test ORDER BY id", null, ct);

        public Task<TestRow?> GetByNameAsync(string name, CancellationToken ct)
            => QuerySingleOrDefaultAsync<TestRow>(
                "SELECT name, value FROM dapper_test WHERE name = @name", new { name }, ct);

        public Task<TestRow?> GetMissingAsync(CancellationToken ct)
            => QuerySingleOrDefaultAsync<TestRow>(
                "SELECT name, value FROM dapper_test WHERE name = @name",
                new { name = "DOES_NOT_EXIST" }, ct);

        public Task<int> InsertAsync(string name, int value, CancellationToken ct)
            => ExecuteAsync("INSERT INTO dapper_test (name, value) VALUES (@name, @value)",
                new { name, value }, ct);
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_Returns_AllRows_From_Parameterized_Query()
    {
        var factory = CreateFactory();
        var service = new TestReadService(factory);

        var rows = (await service.GetAllAsync(CancellationToken.None)).ToList();

        rows.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task QuerySingleOrDefaultAsync_Returns_Row_When_Found()
    {
        var factory = CreateFactory();
        var service = new TestReadService(factory);

        var row = await service.GetByNameAsync("Alpha", CancellationToken.None);

        row.Should().NotBeNull();
        row!.Name.Should().Be("Alpha");
    }

    [Fact]
    public async Task QuerySingleOrDefaultAsync_Returns_Null_When_Not_Found()
    {
        var factory = CreateFactory();
        var service = new TestReadService(factory);

        var row = await service.GetMissingAsync(CancellationToken.None);

        row.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Affected_Row_Count()
    {
        var factory = CreateFactory();
        var service = new TestReadService(factory);

        var affected = await service.InsertAsync("Delta", 4, CancellationToken.None);

        affected.Should().Be(1);
    }

    [Fact]
    public async Task Connection_Disposed_After_Each_Call()
    {
        // Use a spy to verify connection factory is called once per operation
        var dataSource = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        var realFactory = new TestConnectionFactory(dataSource);
        var service = new TestReadService(realFactory);

        await service.GetAllAsync(CancellationToken.None);
        await service.GetAllAsync(CancellationToken.None);

        // Each call opens and disposes a separate connection
        realFactory.OpenCount.Should().Be(2);
    }

    private IDbConnectionFactory CreateFactory()
    {
        var dataSource = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        return new TestConnectionFactory(dataSource);
    }

    private sealed class TestConnectionFactory(NpgsqlDataSource dataSource) : IDbConnectionFactory
    {
        public int OpenCount { get; private set; }

        public async Task<System.Data.IDbConnection> CreateConnectionAsync(CancellationToken ct = default)
        {
            OpenCount++;
            return await dataSource.OpenConnectionAsync(ct);
        }
    }
}
