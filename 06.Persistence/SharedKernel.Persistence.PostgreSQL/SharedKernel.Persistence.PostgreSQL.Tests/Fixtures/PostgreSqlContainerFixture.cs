using Testcontainers.PostgreSql;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Fixtures;

/// <summary>
/// xUnit class fixture that spins up a PostgreSQL Testcontainer for the test session.
/// </summary>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
