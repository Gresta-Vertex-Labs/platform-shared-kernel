using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore.Tests.Remediation;
using SharedKernel.Persistence.EfCore.Seeding;

namespace SharedKernel.Persistence.EfCore.Tests.Seeding;

/// <summary>
/// <c>AddMigrationOnStartup&lt;TContext&gt;()</c>: the registration guards, the start-time validator (pooling, history
/// table) and the hosted-service sequence on SQLite. The PostgreSQL behaviour (role split, migration data source,
/// cross-replica lock) is proven in <c>EfCore.Integration.Tests</c>.
/// </summary>
public sealed class PackageContextMigrationTests
{
    private static void Quiet(DbContextOptionsBuilder o) =>
        o.ConfigureWarnings(w => w
            .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)
            .Ignore(RelationalEventId.PendingModelChangesWarning));

    private static string TempDatabase() => Path.Combine(Path.GetTempPath(), $"sk_pkgmig_{Guid.NewGuid():N}.db");

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    private static void AddUnitContext(IServiceCollection services, string file, string? historyTable) =>
        services.AddDbContext<UnitPackageContext>(o =>
        {
            o.UseSqlite($"Data Source={file};Pooling=False", sqlite =>
            {
                if (historyTable is not null)
                    sqlite.MigrationsHistoryTable(historyTable);
            });
            Quiet(o);
        });

    private static ValidateOptionsResult Validate<TContext>(IServiceCollection services)
        where TContext : DbContext
    {
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IValidateOptions<PackageContextMigrationCheck<TContext>>>()
            .Validate(null, new PackageContextMigrationCheck<TContext>());
    }

    // ---- registration guards ----

    [Fact]
    public void NullServices_Throws()
    {
        Action act = () => PostgresPersistenceExtensions.AddMigrationOnStartup<UnitPackageContext>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveLockTimeout_Throws(int seconds)
    {
        Action act = () => Services().AddMigrationOnStartup<UnitPackageContext>(TimeSpan.FromSeconds(seconds));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("lockTimeout");
    }

    [Fact]
    public void DuplicateRegistration_Throws()
    {
        var services = Services();
        services.AddMigrationOnStartup<UnitPackageContext>();

        Action act = () => services.AddMigrationOnStartup<UnitPackageContext>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*already registered for migration on startup*");
    }

    [Fact]
    public void ASharedKernelDbContext_IsRejected_WithTheRemedy()
    {
        Action act = () => Services().AddMigrationOnStartup<R1PlainContext>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*MigrateOnStartup()*");
    }

    [Fact]
    public void NotCallingTheMethod_RegistersNoHostedServiceAndNoStartupStep()
    {
        var services = Services();
        AddUnitContext(services, TempDatabase(), historyTable: null);

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().Should().BeEmpty();
        provider.GetService<IPersistenceStartup>().Should().BeNull("only a registration that migrates adds the startup signal");
    }

    [Fact]
    public void Registering_AddsOneHostedService_AndAnExpectedStartupStep()
    {
        var services = Services();
        AddUnitContext(services, TempDatabase(), "__pkg_history");
        services.AddMigrationOnStartup<UnitPackageContext>(TimeSpan.FromSeconds(30));

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().OfType<ContextMigrationHostedService<UnitPackageContext>>().Should().ContainSingle();
        provider.GetRequiredService<IPersistenceStartup>().IsCompleted.Should().BeFalse("the migration has not run yet");
    }

    // ---- start-time validation ----

    [Fact]
    public void DefaultHistoryTable_IsRejected_WithoutLeakingTheConnectionString()
    {
        var file = TempDatabase();
        var services = Services();
        AddUnitContext(services, file, historyTable: null);
        services.AddMigrationOnStartup<UnitPackageContext>();

        var result = Validate<UnitPackageContext>(services);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("default migrations history table").And.Contain("MigrationsHistoryTable");
        result.FailureMessage.Should().NotContain(file).And.NotContain("Data Source");
        result.FailureMessage.Should().EndWith(".");
    }

    [Fact]
    public void ExplicitDefaultHistoryTableName_InThePublicSchema_IsRejected()
    {
        var services = Services();
        AddUnitContext(services, TempDatabase(), HistoryRepository.DefaultTableName);
        services.AddMigrationOnStartup<UnitPackageContext>();

        Validate<UnitPackageContext>(services).Failed.Should().BeTrue();
    }

    [Fact]
    public void AHistoryTableOfItsOwn_IsAccepted()
    {
        var services = Services();
        AddUnitContext(services, TempDatabase(), "__pkg_history");
        services.AddMigrationOnStartup<UnitPackageContext>();

        Validate<UnitPackageContext>(services).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void APooledContext_IsRejected_NamingTheRemedy()
    {
        var services = Services();
        services.AddDbContextPool<UnitPackageContext>(o =>
        {
            o.UseSqlite($"Data Source={TempDatabase()};Pooling=False", sqlite => sqlite.MigrationsHistoryTable("__pkg_history"));
            Quiet(o);
        });
        services.AddMigrationOnStartup<UnitPackageContext>();

        var result = Validate<UnitPackageContext>(services);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("pooled").And.Contain("AddDbContext");
        result.FailureMessage.Should().EndWith(".");
    }

    // ---- the hosted-service sequence ----

    [Fact]
    public async Task Starting_AppliesTheMigrations_AndCompletesThePersistenceStartup()
    {
        var file = TempDatabase();
        try
        {
            var services = Services();
            AddUnitContext(services, file, "__pkg_history");
            services.AddMigrationOnStartup<UnitPackageContext>();
            await using var provider = services.BuildServiceProvider();

            foreach (var hosted in provider.GetServices<IHostedService>())
                await hosted.StartAsync(CancellationToken.None);

            provider.GetRequiredService<IPersistenceStartup>().IsCompleted.Should().BeTrue();
            await provider.GetRequiredService<IPersistenceStartup>().WaitAsync();

            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<UnitPackageContext>();
            (await context.Database.GetAppliedMigrationsAsync()).Should().Equal("20260101000000_UnitPackageInit");
            (await context.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM sqlite_master WHERE name = 'unit_package_things'").SingleAsync())
                .Should().Be(1);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task AFailingMigration_FailsStart_AndFaultsThePersistenceStartup()
    {
        var file = TempDatabase();
        try
        {
            var services = Services();
            services.AddDbContext<FailingPackageContext>(o =>
            {
                o.UseSqlite($"Data Source={file};Pooling=False", sqlite => sqlite.MigrationsHistoryTable("__pkg_failing_history"));
                Quiet(o);
            });
            services.AddMigrationOnStartup<FailingPackageContext>();
            await using var provider = services.BuildServiceProvider();

            var hosted = provider.GetServices<IHostedService>().Single();
            await FluentActions.Awaiting(() => hosted.StartAsync(CancellationToken.None)).Should().ThrowAsync<Exception>();

            var startup = provider.GetRequiredService<IPersistenceStartup>();
            startup.IsCompleted.Should().BeFalse();
            await FluentActions.Awaiting(() => startup.WaitAsync()).Should().ThrowAsync<Exception>();
        }
        finally
        {
            File.Delete(file);
        }
    }
}

public sealed class UnitPackageContext(DbContextOptions<UnitPackageContext> options) : DbContext(options);

[DbContext(typeof(UnitPackageContext))]
[Migration("20260101000000_UnitPackageInit")]
public sealed class UnitPackageInit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("CREATE TABLE unit_package_things (id INTEGER PRIMARY KEY);");
}

public sealed class FailingPackageContext(DbContextOptions<FailingPackageContext> options) : DbContext(options);

[DbContext(typeof(FailingPackageContext))]
[Migration("20260101000001_FailingPackageInit")]
public sealed class FailingPackageInit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("THIS IS NOT SQL;");
}
