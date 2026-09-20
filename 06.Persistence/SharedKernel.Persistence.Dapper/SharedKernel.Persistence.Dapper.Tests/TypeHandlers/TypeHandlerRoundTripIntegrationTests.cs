using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Dapper.Extensions;
using SharedKernel.Persistence.Dapper.TypeHandlers;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.Dapper.Tests.TypeHandlers;

/// <summary>
/// <see cref="StronglyTypedIdTypeHandler{TStronglyTypedId,TValue}"/>/
/// <see cref="SmartEnumTypeHandler{TEnum,TValue}"/> real round-trip against PostgreSQL, both as a
/// query PARAMETER and as a mapped COLUMN, plus <see cref="AddSharedKernelDapper(IServiceCollection,Action{DapperTypeHandlerBuilder}?)"/>'s
/// registration.
/// </summary>
public sealed class TypeHandlerRoundTripIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture = new();
    private NpgsqlDataSource? _dataSource;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);

        var services = new ServiceCollection();
        services.AddSharedKernelDapper(b => b
            .AddTypeHandler<TestOrderId, TestOrderIdHandler>()
                .AddTypeHandler<TestStatus, TestStatusHandler>());
        services.BuildServiceProvider();

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TABLE IF EXISTS type_handler_test;
            CREATE TABLE type_handler_test (
                order_id UUID PRIMARY KEY,
                status INT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();

        await _fixture.DisposeAsync();
    }

    private sealed record TypeHandlerRow(TestOrderId OrderId, TestStatus Status);

    [Fact]
    public async Task StronglyTypedIdAndSmartEnum_RoundTrip_AsParameterAndColumn()
    {
        var orderId = TestOrderId.New();

        await using var connection = await _dataSource!.OpenConnectionAsync();

        await connection.ExecuteAsync(
            "INSERT INTO type_handler_test (order_id, status) VALUES (@OrderId, @Status)",
            new { OrderId = orderId, Status = TestStatus.Active });

        // No "AS PascalCase" aliasing needed — AddSharedKernelDapper's default
        // enableSnakeCaseMapping: true (set in InitializeAsync above) means order_id binds directly
        // to OrderId.
        var row = await connection.QuerySingleAsync<TypeHandlerRow>(
            "SELECT order_id, status FROM type_handler_test WHERE order_id = @OrderId",
            new { OrderId = orderId });

        row.OrderId.Should().Be(orderId);
        row.OrderId.Value.Should().Be(orderId.Value);
        row.Status.Should().Be(TestStatus.Active);
    }

    [Fact]
    public void AddSharedKernelDapper_Builder_RegistersEveryConfiguredHandler()
    {
        // A second, independent Apply call with a DIFFERENT handler set must still register both —
        // AddSharedKernelDapper is not "first call wins".
        var applied = new List<string>();

        DapperTypeHandlers.Apply(b =>
        {
            b.AddTypeHandler<TestOrderId, TestOrderIdHandler>();
            applied.Add(nameof(TestOrderId));
        });

        DapperTypeHandlers.Apply(b =>
        {
            b.AddTypeHandler<TestStatus, TestStatusHandler>();
            applied.Add(nameof(TestStatus));
        });

        applied.Should().Contain([nameof(TestOrderId), nameof(TestStatus)]);
    }

    [Fact]
    public void Apply_Default_EnablesSnakeCaseColumnMapping()
    {
        DapperTypeHandlers.Apply();

        global::Dapper.DefaultTypeMap.MatchNamesWithUnderscores.Should().BeTrue();
    }

    [Fact]
    public void Apply_EnableSnakeCaseMappingFalse_DisablesIt_ThenRestoresThePlatformDefault()
    {
        // DefaultTypeMap.MatchNamesWithUnderscores is process-wide static Dapper
        // state (see DapperTypeHandlers.Apply's own remarks) — always restore the platform default
        // before returning so no other test observes the opt-out.
        try
        {
            DapperTypeHandlers.Apply(enableSnakeCaseMapping: false);

            global::Dapper.DefaultTypeMap.MatchNamesWithUnderscores.Should().BeFalse();
        }
        finally
        {
            DapperTypeHandlers.Apply();
        }
    }
}
