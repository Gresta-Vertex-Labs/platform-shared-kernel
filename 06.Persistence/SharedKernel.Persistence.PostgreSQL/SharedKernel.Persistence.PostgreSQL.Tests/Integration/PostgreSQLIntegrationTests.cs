using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.PostgreSQL.Connections;
using SharedKernel.Persistence.PostgreSQL.Conventions;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Persistence.PostgreSQL.Jsonb;
using SharedKernel.Persistence.PostgreSQL.Vector;
using Testcontainers.PostgreSql;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Integration;

/// <summary>
/// T-38 (2-5): PostgreSQL integration tests — JSONB, pgvector, NpgsqlConnectionFactory,
/// and AddSharedKernelPostgreSQL smoke.
/// All tests require a real PostgreSQL Testcontainer.
/// </summary>
/// <remarks>
/// WO-053/P-336: deliberately does NOT join the shared <c>[Collection("PostgreSQL")]</c>/
/// <see cref="PostgreSqlTestCollection"/> fixture the other three PostgreSQL.Tests integration
/// classes were migrated onto — this class's pgvector round-trip test requires the
/// <c>pgvector/pgvector:pg16</c> image, which the shared fixture's plain <c>postgres:16.4</c> image
/// does not provide (the pgvector extension binary/shared library is absent from a vanilla
/// PostgreSQL image, so <c>CREATE EXTENSION vector</c> cannot succeed against it). Continues to
/// manage its own dedicated, pgvector-enabled container.
/// </remarks>
public sealed class PostgreSQLIntegrationTests : IAsyncLifetime
{
    // Use pgvector image so vector extension is available for T-38(3)
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    private string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    // -----------------------------------------------------------------------
    // T-38(4): NpgsqlConnectionFactory returns open connection
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NpgsqlConnectionFactory_CreateConnectionAsync_Returns_Open_Connection()
    {
        var dataSource = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        var factory = new NpgsqlConnectionFactory(dataSource);

        using var connection = await factory.CreateConnectionAsync(CancellationToken.None);

        connection.Should().NotBeNull();
        connection.State.Should().Be(System.Data.ConnectionState.Open);
    }

    // -----------------------------------------------------------------------
    // T-38(5): AddSharedKernelPostgreSQL smoke — IDbConnectionFactory resolves
    // -----------------------------------------------------------------------

    [Fact]
    public void AddSharedKernelPostgreSQL_Resolves_IDbConnectionFactory_As_NpgsqlConnectionFactory()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelPostgreSQL(ConnectionString);

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        factory.Should().BeOfType<NpgsqlConnectionFactory>();
    }

    // -----------------------------------------------------------------------
    // T-38(2): JSONB round-trip
    // -----------------------------------------------------------------------

    [Fact]
    public async Task JSONB_Column_RoundTrip_Serializes_And_Deserializes_Correctly()
    {
        var dataSource = new NpgsqlDataSourceBuilder(ConnectionString)
            .EnableDynamicJson()
            .Build();

        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();

        // Create table with jsonb column
        cmd.CommandText = """
            DROP TABLE IF EXISTS jsonb_test;
            CREATE TABLE jsonb_test (
                id SERIAL PRIMARY KEY,
                data JSONB NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();

        // Insert JSON data
        cmd.CommandText = "INSERT INTO jsonb_test (data) VALUES (@data::jsonb) RETURNING id";
        var jsonData = JsonSerializer.Serialize(new { Name = "Test", Value = 42 });
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@data", jsonData);
        var id = (int)(await cmd.ExecuteScalarAsync())!;

        // Fetch back and verify
        cmd.CommandText = "SELECT data::text FROM jsonb_test WHERE id = @id";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@id", id);
        var result = (string)(await cmd.ExecuteScalarAsync())!;

        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(result)!;
        parsed["Name"].GetString().Should().Be("Test");
        parsed["Value"].GetInt32().Should().Be(42);
    }

    // -----------------------------------------------------------------------
    // T-38(3): pgvector round-trip
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VectorColumn_RoundTrip_Stores_And_Retrieves_Correct_Dimensions()
    {
        var dataSource = new NpgsqlDataSourceBuilder(ConnectionString).Build();

        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();

        // Enable pgvector extension
        cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS vector;";
        await cmd.ExecuteNonQueryAsync();

        // Create table with vector column
        cmd.CommandText = """
            DROP TABLE IF EXISTS vector_test;
            CREATE TABLE vector_test (
                id SERIAL PRIMARY KEY,
                embedding vector(3) NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();

        // Insert vector
        cmd.CommandText = "INSERT INTO vector_test (embedding) VALUES ('[1.0, 2.0, 3.0]'::vector) RETURNING id";
        cmd.Parameters.Clear();
        var id = (int)(await cmd.ExecuteScalarAsync())!;

        // Fetch and verify dimensions
        cmd.CommandText = "SELECT vector_dims(embedding) FROM vector_test WHERE id = @id";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@id", id);
        var dims = (int)(await cmd.ExecuteScalarAsync())!;

        dims.Should().Be(3);
    }
}
