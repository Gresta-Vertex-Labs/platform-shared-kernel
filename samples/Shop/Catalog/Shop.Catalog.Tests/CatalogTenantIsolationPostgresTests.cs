using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Domain.Monetary;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Transactions;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Testing;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Execution;
using Shop.Catalog.Domain;
using Shop.Catalog.Infrastructure.Persistence;
using Xunit;

namespace Shop.Catalog.Tests;

/// <summary>A fact that runs only when <c>SHOP_POSTGRES=1</c>: it starts a PostgreSQL container (Docker).</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SHOP_POSTGRES") != "1")
        {
            Skip =
                "PostgreSQL integration tests run with SHOP_POSTGRES=1 and Docker (see samples/Shop/README.md).";
        }
    }
}

public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgresTestServer Server { get; private set; } = null!;

    public async Task InitializeAsync() => Server = await PostgresTestServer.StartAsync();

    public async Task DisposeAsync() => await Server.DisposeAsync();
}

/// <summary>
/// Row-level security against real PostgreSQL: the catalog runs as the production runtime role, so a product written
/// for one tenant is invisible to another even without the context's query filter.
/// </summary>
public sealed class CatalogTenantIsolationPostgresTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>
{
    private static readonly TenantId Contoso = new(
        Guid.Parse("0f8d3c1e-7a52-4b6e-9d1f-2c4e6a8b0d13")
    );
    private static readonly TenantId Fabrikam = new(
        Guid.Parse("5b7e9f21-3c64-4d8a-a1e3-6f8b0c2d4e57")
    );

    [PostgresFact]
    public async Task A_product_written_for_one_tenant_is_invisible_to_another()
    {
        await using var database = await fixture.Server.CreateDatabaseAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new FakeClock());
        services.AddSharedKernelPostgres<CatalogDbContext>(
            database.BuildConfiguration(CatalogDatabase.ConnectionName),
            CatalogDatabase.ConnectionName,
            persistence => CatalogDatabase.Configure(persistence)
        );
        var caller = services.AddTestRequestContext(TestRequestContext.ForTenant(Contoso));
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await database.CreateSchemaAsync(context); // as the migrator role, like a migration
            await database.EnableRowLevelSecurityAsync(context); // the tenant policy on every tenanted table
        }

        Guid productId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var product = Product
                .Create(
                    Contoso,
                    new ProductDetails("KB-001", "Keyboard", "Mechanical.", "Acme", "Peripherals"),
                    Money.Create(49m, Currency.Eur).Value,
                    new FakeClock()
                )
                .Value;
            context.Products.Add(product);
            await context.SaveChangesAsync();
            productId = product.Id.Value;
        }

        // Raw SQL skips the context's tenant query filter, so only the database policy can hide the row.
        caller.TenantId = Fabrikam;
        (await CountProductsAsync(provider))
            .Should()
            .Be(0, "row-level security hides another tenant's rows");

        caller.TenantId = Contoso;
        (await CountProductsAsync(provider)).Should().Be(1);
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            (await context.Products.SingleAsync()).Id.Value.Should().Be(productId);
        }
    }

    private static async Task<int> CountProductsAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        // The unit of work binds the caller's tenant to the transaction, as every command does.
        return await scope
            .ServiceProvider.GetRequiredService<IUnitOfWork>()
            .ExecuteInTransactionAsync(ct =>
                context
                    .Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM products")
                    .SingleAsync(ct)
            );
    }
}
