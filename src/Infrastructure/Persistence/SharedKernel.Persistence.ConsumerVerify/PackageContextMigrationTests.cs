using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore.Seeding;
using SharedKernel.Persistence.Testing;
using Xunit;

namespace SharedKernel.Persistence.ConsumerVerify;

/// <summary>A plain context a kernel package would ship, with its own history table and one hand-written migration.</summary>
public sealed class ShippedPackageContext(DbContextOptions<ShippedPackageContext> options) : DbContext(options);

[DbContext(typeof(ShippedPackageContext))]
[Migration("20260101000000_ShippedPackageInit")]
public sealed class ShippedPackageInit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("CREATE TABLE shipped_package_things (id uuid PRIMARY KEY);");
}

public sealed class PackageContextMigrationTests
{
    // AddMigrationOnStartup on the packed packages, under the role split: the migrator creates the schema, the host
    // reports ready afterwards, and the runtime role can use but not create objects.
    [Fact]
    public async Task AddMigrationOnStartup_MigratesAPackageContextAsTheMigrator_BeforeThePersistenceStartupCompletes()
    {
        await using var server = await PostgresTestServer.StartAsync();
        await using var database = await server.CreateDatabaseAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:pkg"] = database.RuntimeConnectionString,
                ["SharedKernel:Persistence:pkg:MigrationConnectionString"] = database.MigratorConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelNpgsql(configuration, "pkg");
        services.AddDbContext<ShippedPackageContext>((sp, options) =>
            options
                .UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>(), npgsql => npgsql.MigrationsHistoryTable("__ef_shipped_package"))
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddMigrationOnStartup<ShippedPackageContext>();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.False(provider.GetRequiredService<IPersistenceStartup>().IsCompleted);

        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);

        Assert.True(provider.GetRequiredService<IPersistenceStartup>().IsCompleted);

        await using var admin = NpgsqlDataSource.Create(database.AdminConnectionString);
        await using (var owner = admin.CreateCommand("SELECT tableowner FROM pg_tables WHERE tablename = 'shipped_package_things'"))
            Assert.Equal(PostgresTestRoles.Migrator, (string?)await owner.ExecuteScalarAsync());

        await using (var history = admin.CreateCommand("SELECT count(*) FROM \"__ef_shipped_package\""))
            Assert.Equal(1L, (long)(await history.ExecuteScalarAsync())!);

        await using var runtime = NpgsqlDataSource.Create(database.RuntimeConnectionString);
        await using (var write = runtime.CreateCommand("INSERT INTO shipped_package_things VALUES (gen_random_uuid())"))
            Assert.Equal(1, await write.ExecuteNonQueryAsync());

        await using var create = runtime.CreateCommand("CREATE TABLE runtime_made (id int)");
        var denied = await Assert.ThrowsAsync<PostgresException>(() => create.ExecuteNonQueryAsync());
        Assert.Equal("42501", denied.SqlState);
    }

    [Fact]
    public void AddMigrationOnStartup_OnTheDefaultHistoryTable_FailsStartup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ShippedPackageContext>(options => options.UseNpgsql("Host=localhost;Port=1;Database=x;Username=x;Password=x"));
        services.AddMigrationOnStartup<ShippedPackageContext>();

        using var provider = services.BuildServiceProvider();

        var failure = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("default migrations history table", failure.Message);
    }
}
