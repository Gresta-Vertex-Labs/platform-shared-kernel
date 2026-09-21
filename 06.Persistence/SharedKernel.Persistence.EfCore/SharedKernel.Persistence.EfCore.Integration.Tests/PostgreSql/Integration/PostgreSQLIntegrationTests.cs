using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Npgsql.Connections;
using SharedKernel.Persistence.Npgsql.Extensions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Testing.Persistence;
using SharedKernel.Persistence.EfCore.Conventions;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Jsonb;
using SharedKernel.Persistence.EfCore.Vectors;
using Testcontainers.PostgreSql;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.PostgreSql.Integration;

/// <summary>
/// T-38 (2-5): PostgreSQL integration tests — JSONB, pgvector, NpgsqlConnectionFactory,
/// the shared data source, and pgvector through the DI data source.
/// All tests require a real PostgreSQL Testcontainer.
/// </summary>
/// <remarks>
/// Deliberately does NOT join the shared <c>[Collection("PostgreSQL")]</c>/
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
    // T-38(5): shared data source smoke — IDbConnectionFactory resolves
    // -----------------------------------------------------------------------

    [Fact]
    public void AddSharedKernelNpgsql_Resolves_IDbConnectionFactory_As_NpgsqlConnectionFactory()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelNpgsql(TestNpgsqlConfiguration.Create(ConnectionString));

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        factory.Should().BeOfType<NpgsqlConnectionFactory>();
    }

    // -----------------------------------------------------------------------
    // P-558 / A32: pgvector through the SHARED, DI-registered data source. The ADO-level UseVector()
    // must be applied to the data source AddSharedKernelNpgsql builds (NpgsqlPersistenceOptions.UseVector);
    // UsePostgreSQL(sp) then turns on the EF Core mapping automatically.
    // -----------------------------------------------------------------------

    private async Task CreateVectorExtensionAsync()
    {
        // Before the shared data source opens its first connection: Npgsql resolves the vector type
        // once per data source.
        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS vector;";
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task VectorEntity_ThroughSharedDiDataSource_WritesAndReadsVector()
    {
        await CreateVectorExtensionAsync();

        var services = new ServiceCollection();
        services.AddSharedKernelNpgsql(TestNpgsqlConfiguration.Create(ConnectionString, useVector: true));
        // A hand-built registration over the shared data source (the path a design-time factory or another
        // package takes): the public UsePostgreSQL(sp) plus PersistenceContextDependencies.Create().
        services.AddSingleton(PersistenceContextDependencies.Create());
        services.AddDbContext<VectorDiTestDbContext>((sp, options) => options.UsePostgreSQL(sp));

        await using var provider = services.BuildServiceProvider();

        var id = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<VectorDiTestDbContext>();
            await ctx.Database.EnsureCreatedAsync();
            ctx.Items.Add(new VectorDiItem { Id = id, Embedding = new Pgvector.Vector(new[] { 1f, 2f, 3f }) });
            await ctx.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<VectorDiTestDbContext>();
            var item = await ctx.Items.SingleAsync(i => i.Id == id);
            item.Embedding.ToArray().Should().Equal(1f, 2f, 3f);
        }
    }

    [Fact]
    public void UsePostgreSQL_VectorRequested_ButSharedDataSourceBuiltWithoutIt_FailsWithGuidance()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelNpgsql(TestNpgsqlConfiguration.Create(ConnectionString, useVector: false));
        services.AddSingleton(PersistenceContextDependencies.Create());
        services.AddDbContext<VectorDiTestDbContext>((sp, options) => options.UsePostgreSQL(sp, o => o.UseVector = true));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<VectorDiTestDbContext>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*UseVector*");
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

/// <summary>Minimal SharedKernel context for the shared-data-source pgvector proof (A32).</summary>
public sealed class VectorDiTestDbContext : SharedKernelDbContext
{
    public DbSet<VectorDiItem> Items => Set<VectorDiItem>();

    public VectorDiTestDbContext(
        DbContextOptions<VectorDiTestDbContext> options,
        PersistenceContextDependencies dependencies)
        : base(options, dependencies)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VectorDiItem>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasVectorColumn(e => e.Embedding, dimensions: 3);
        });
    }
}

public sealed class VectorDiItem
{
    public Guid Id { get; set; }
    public Pgvector.Vector Embedding { get; set; } = null!;
}
