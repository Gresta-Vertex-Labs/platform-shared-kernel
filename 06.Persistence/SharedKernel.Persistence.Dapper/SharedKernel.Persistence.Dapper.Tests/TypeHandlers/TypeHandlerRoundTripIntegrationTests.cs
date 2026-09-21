using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using SharedKernel.Persistence.Dapper.Extensions;
using Testcontainers.PostgreSql;

namespace SharedKernel.Persistence.Dapper.Tests.TypeHandlers;

/// <summary>
/// Every handler registered through <c>AddSharedKernelDapper</c> round-trips against PostgreSQL (with pgvector),
/// as a parameter and as a mapped column, with snake_case columns mapped without aliases.
/// </summary>
[Collection(DapperConfigurationCollection.Name)]
public sealed class TypeHandlerRoundTripIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
    private NpgsqlDataSource? _dataSource;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var builder = new NpgsqlDataSourceBuilder(_container.GetConnectionString());
        builder.UseVector();
        _dataSource = builder.Build();

        new ServiceCollection().AddSharedKernelDapper(b => b
            .AddStronglyTypedId<TestOrderId, Guid>()
            .AddSmartEnum<TestStatus, int>()
            .AddJsonb(TestJsonContext.Default.TestAddress));

        await using (var extension = _dataSource.CreateCommand("CREATE EXTENSION IF NOT EXISTS vector"))
            await extension.ExecuteNonQueryAsync();

        // Type loading happens per connection; reload after creating the extension.
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ReloadTypesAsync();
        await connection.ExecuteAsync("""
            CREATE TABLE type_handler_test (
                order_id uuid PRIMARY KEY,
                status int NOT NULL,
                shipping_address jsonb NOT NULL,
                embedding vector(3) NOT NULL
            );
            """);
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();

        await _container.DisposeAsync();
    }

    private sealed record TypeHandlerRow(TestOrderId OrderId, TestStatus Status, TestAddress ShippingAddress, Vector Embedding);

    [Fact]
    public async Task EveryHandler_RoundTrips_AsParameterAndColumn()
    {
        var orderId = TestOrderId.New();
        var address = new TestAddress("Main 1", "Izmir");
        var embedding = new Vector(new float[] { 1, 2, 3 });

        await using var connection = await _dataSource!.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO type_handler_test (order_id, status, shipping_address, embedding) VALUES (@OrderId, @Status, @Address, @Embedding)",
            new { OrderId = orderId, Status = TestStatus.Active, Address = address, Embedding = embedding });

        var row = await connection.QuerySingleAsync<TypeHandlerRow>(
            "SELECT order_id, status, shipping_address, embedding FROM type_handler_test WHERE order_id = @OrderId "
                + "AND embedding <-> @Embedding < 0.001",
            new { OrderId = orderId, Embedding = embedding });

        row.OrderId.Should().Be(orderId);
        row.Status.Should().Be(TestStatus.Active);
        row.ShippingAddress.Should().Be(address);
        row.Embedding.ToArray().Should().Equal(1, 2, 3);
    }
}

/// <summary>Serializes the tests that change Dapper's process-wide configuration.</summary>
[CollectionDefinition(Name)]
public sealed class DapperConfigurationCollection
{
    public const string Name = "Dapper process-wide configuration";
}
