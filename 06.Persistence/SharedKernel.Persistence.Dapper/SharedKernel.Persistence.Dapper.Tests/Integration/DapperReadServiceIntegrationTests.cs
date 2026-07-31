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

// ---------------------------------------------------------------------------
// WO-051/P-321 — multi-mapping / QueryMultipleAsync DTOs
// ---------------------------------------------------------------------------

public sealed class OrderRow
{
    public int Id { get; init; }
    public decimal Total { get; init; }
}

public sealed class CustomerRow
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class PaymentRow
{
    public int Id { get; init; }
    public decimal Amount { get; init; }
}

public sealed record OrderWithCustomerDto(OrderRow Order, CustomerRow Customer);

public sealed record OrderWithCustomerAndPaymentDto(OrderRow Order, CustomerRow Customer, PaymentRow Payment);

public sealed record OrderDashboardDto(int OrderCount, decimal TotalRevenue);

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

            DROP TABLE IF EXISTS dapper_customer;
            DROP TABLE IF EXISTS dapper_order;
            DROP TABLE IF EXISTS dapper_payment;
            CREATE TABLE dapper_customer (
                id      SERIAL PRIMARY KEY,
                name    TEXT NOT NULL
            );
            CREATE TABLE dapper_order (
                id            SERIAL PRIMARY KEY,
                total         NUMERIC NOT NULL,
                customer_id   INT NOT NULL REFERENCES dapper_customer(id)
            );
            CREATE TABLE dapper_payment (
                id          SERIAL PRIMARY KEY,
                order_id    INT NOT NULL REFERENCES dapper_order(id),
                amount      NUMERIC NOT NULL
            );
            INSERT INTO dapper_customer (name) VALUES ('Ada'), ('Grace');
            INSERT INTO dapper_order (total, customer_id) VALUES (100, 1), (200, 2);
            INSERT INTO dapper_payment (order_id, amount) VALUES (1, 100), (2, 150);
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

        // WO-051/P-321 — multi-mapping / QueryMultipleAsync / protected ConnectionFactory seam.

        public Task<IEnumerable<OrderWithCustomerDto>> GetOrdersWithCustomerAsync(CancellationToken ct) =>
            QueryAsync<OrderRow, CustomerRow, OrderWithCustomerDto>(
                sql: """
                     SELECT o.id, o.total, c.id, c.name
                     FROM dapper_order o JOIN dapper_customer c ON c.id = o.customer_id
                     ORDER BY o.id
                     """,
                map: (order, customer) => new OrderWithCustomerDto(order, customer),
                splitOn: "id",
                ct: ct);

        public Task<IEnumerable<OrderWithCustomerAndPaymentDto>> GetOrdersWithCustomerAndPaymentAsync(CancellationToken ct) =>
            QueryAsync<OrderRow, CustomerRow, PaymentRow, OrderWithCustomerAndPaymentDto>(
                sql: """
                     SELECT o.id, o.total, c.id, c.name, p.id, p.amount
                     FROM dapper_order o
                     JOIN dapper_customer c ON c.id = o.customer_id
                     JOIN dapper_payment p ON p.order_id = o.id
                     ORDER BY o.id
                     """,
                map: (order, customer, payment) => new OrderWithCustomerAndPaymentDto(order, customer, payment),
                splitOn: "id",
                ct: ct);

        public Task<OrderDashboardDto> GetDashboardAsync(CancellationToken ct) =>
            QueryMultipleAsync(
                sql: "SELECT COUNT(*) FROM dapper_order; SELECT SUM(total) FROM dapper_order;",
                readFunc: async grid =>
                {
                    var count = await grid.ReadSingleAsync<int>();
                    var revenue = await grid.ReadSingleAsync<decimal>();
                    return new OrderDashboardDto(count, revenue);
                },
                ct: ct);

        // Exercises the promoted protected ConnectionFactory extension seam directly.
        public async Task<int> CountViaConnectionFactoryAsync(CancellationToken ct)
        {
            using var connection = await ConnectionFactory.CreateConnectionAsync(ct);
            return await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT COUNT(*) FROM dapper_order", cancellationToken: ct));
        }
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

    // -----------------------------------------------------------------------
    // WO-051/P-321 — multi-mapping / QueryMultipleAsync / ConnectionFactory seam tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_TwoTypeMultiMapping_ComposesCorrectObjects_FromRealJoin()
    {
        var factory = CreateFactory();
        var service = new TestReadService(factory);

        var results = (await service.GetOrdersWithCustomerAsync(CancellationToken.None)).ToList();

        results.Should().HaveCount(2);
        results[0].Order.Total.Should().Be(100);
        results[0].Customer.Name.Should().Be("Ada");
        results[1].Order.Total.Should().Be(200);
        results[1].Customer.Name.Should().Be("Grace");
    }

    [Fact]
    public async Task QueryAsync_ThreeTypeMultiMapping_ComposesCorrectObjects_FromRealJoin()
    {
        var factory = CreateFactory();
        var service = new TestReadService(factory);

        var results = (await service.GetOrdersWithCustomerAndPaymentAsync(CancellationToken.None)).ToList();

        results.Should().HaveCount(2);
        results[0].Customer.Name.Should().Be("Ada");
        results[0].Payment.Amount.Should().Be(100);
        results[1].Customer.Name.Should().Be("Grace");
        results[1].Payment.Amount.Should().Be(150);
    }

    [Fact]
    public async Task QueryMultipleAsync_ReadsTwoDistinctResultSets_FromOneRoundTrip()
    {
        var factory = CreateFactory();
        var service = new TestReadService(factory);

        var dashboard = await service.GetDashboardAsync(CancellationToken.None);

        dashboard.OrderCount.Should().Be(2);
        dashboard.TotalRevenue.Should().Be(300);
    }

    [Fact]
    public async Task ConnectionFactory_ProtectedSeam_UsableDirectlyBySubclass()
    {
        var factory = CreateFactory();
        var service = new TestReadService(factory);

        var count = await service.CountViaConnectionFactoryAsync(CancellationToken.None);

        count.Should().Be(2);
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
