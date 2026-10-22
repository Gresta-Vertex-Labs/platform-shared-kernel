using System.Collections.Concurrent;
using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Seeding;
using SharedKernel.Persistence.Npgsql.Coordination;
using SharedKernel.Persistence.Testing;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.Postgres;

// ---------------------------------------------------------------------------------------------------------
// Fixtures: plain contexts with hand-written migrations and their own history tables (what a kernel package ships).
// Each scenario has its own context type so its migration is discovered for that context only.
// ---------------------------------------------------------------------------------------------------------

public sealed class PkgSplitContext(DbContextOptions<PkgSplitContext> options) : DbContext(options);

[DbContext(typeof(PkgSplitContext))]
[Migration("20260101000000_PkgSplitInit")]
public sealed class PkgSplitInit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("CREATE TABLE pkg_split_things (id uuid PRIMARY KEY, name text NOT NULL);");
}

public sealed class PkgConcurrentContext(DbContextOptions<PkgConcurrentContext> options) : DbContext(options);

[DbContext(typeof(PkgConcurrentContext))]
[Migration("20260101000000_PkgConcurrentInit")]
public sealed class PkgConcurrentInit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("SELECT pg_sleep(1.5);");
        migrationBuilder.Sql("CREATE TABLE pkg_concurrent_things (id uuid PRIMARY KEY);");
    }
}

public sealed class PkgLockedContext(DbContextOptions<PkgLockedContext> options) : DbContext(options);

[DbContext(typeof(PkgLockedContext))]
[Migration("20260101000000_PkgLockedInit")]
public sealed class PkgLockedInit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("CREATE TABLE pkg_locked_things (id uuid PRIMARY KEY);");
}

public sealed class PkgCoexistContext(DbContextOptions<PkgCoexistContext> options) : DbContext(options);

[DbContext(typeof(PkgCoexistContext))]
[Migration("20260101000000_PkgCoexistInit")]
public sealed class PkgCoexistInit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("CREATE TABLE pkg_coexist_things (id uuid PRIMARY KEY);");
}

public sealed class PkgDefaultHistoryContext(DbContextOptions<PkgDefaultHistoryContext> options) : DbContext(options);

public sealed class PkgHandContext(DbContextOptions<PkgHandContext> options) : DbContext(options);

[DbContext(typeof(PkgHandContext))]
[Migration("20260101000000_PkgHandInit")]
public sealed class PkgHandInit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("CREATE TABLE pkg_hand_things (id uuid PRIMARY KEY);");
}

/// <summary>A service context (migrated by <c>MigrateOnStartup()</c>) with a migration of its own.</summary>
public sealed class SvcCoexistContext(DbContextOptions<SvcCoexistContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    protected override bool ShouldApplyConfiguration(Type configurationType) => false;
}

[DbContext(typeof(SvcCoexistContext))]
[Migration("20260101000000_SvcCoexistInit")]
public sealed class SvcCoexistInit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("CREATE TABLE svc_coexist_things (id uuid PRIMARY KEY);");
}

/// <summary>
/// <c>AddMigrationOnStartup&lt;TContext&gt;()</c> against a real PostgreSQL under the canonical role split: the
/// migration runs as the migrator, never on the runtime connection, once across replicas and before
/// <c>IPersistenceStartup</c> completes.
/// </summary>
[Collection("EfCorePostgres")]
public sealed class PackageContextMigrationPostgresTests(PostgreSqlContainerFixture fixture)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    // ---- a started "host": the options validator, then every hosted service in registration order ----

    private sealed class Host(ServiceProvider provider, RecordingLoggerProvider logs, DdlRecorder ddl) : IAsyncDisposable
    {
        public ServiceProvider Provider { get; } = provider;

        public RecordingLoggerProvider Logs { get; } = logs;

        public DdlRecorder Ddl { get; } = ddl;

        public IPersistenceStartup Startup => Provider.GetRequiredService<IPersistenceStartup>();

        public async Task StartAsync()
        {
            Provider.GetService<IStartupValidator>()?.Validate();
            foreach (var hosted in Provider.GetServices<IHostedService>())
                await hosted.StartAsync(CancellationToken.None);
        }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<int> _events = new();

        public IReadOnlyList<int> EventIds => [.. _events];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(_events);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(ConcurrentQueue<int> events) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                events.Enqueue(eventId.Id);
        }
    }

    /// <summary>Records the role every DDL statement of a context runs as.</summary>
    private sealed class DdlRecorder : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<(string? User, string Sql)> _commands = new();

        public IReadOnlyList<string?> DdlUsers =>
            [.. _commands.Where(c => c.Sql.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase)).Select(c => c.User)];

        public int Count(string fragment) => _commands.Count(c => c.Sql.Contains(fragment, StringComparison.Ordinal) && c.Sql.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase));

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command) =>
            _commands.Enqueue((new NpgsqlConnectionStringBuilder(command.Connection!.ConnectionString).Username, command.CommandText));
    }

    private static void Quiet(DbContextOptionsBuilder o) =>
        o.ConfigureWarnings(w => w
            .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)
            .Ignore(RelationalEventId.PendingModelChangesWarning));

    private static IConfiguration Configuration(PostgresTestDatabase db, bool withMigrator)
    {
        var values = new Dictionary<string, string?> { ["ConnectionStrings:pkg"] = db.RuntimeConnectionString };
        if (withMigrator)
            values["SharedKernel:Persistence:pkg:MigrationConnectionString"] = db.MigratorConnectionString;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>A Dapper-style host (Npgsql registration only) with one package context.</summary>
    private static Host PackageHost<TContext>(
        PostgresTestDatabase db,
        string historyTable,
        bool withMigrator = true,
        TimeSpan? lockTimeout = null,
        Action<IServiceCollection>? more = null)
        where TContext : DbContext
    {
        var logs = new RecordingLoggerProvider();
        var ddl = new DdlRecorder();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
        services.AddSharedKernelNpgsql(Configuration(db, withMigrator), "pkg");
        services.AddDbContext<TContext>((sp, o) =>
        {
            o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>(), n => n.MigrationsHistoryTable(historyTable));
            o.AddInterceptors(ddl);
            Quiet(o);
        });
        services.AddMigrationOnStartup<TContext>(lockTimeout);
        more?.Invoke(services);

        return new Host(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), logs, ddl);
    }

    private static async Task<List<string>> HistoryAsync(PostgresTestDatabase db, string table)
    {
        await using var connection = new NpgsqlConnection(db.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT * FROM \"{table}\" ORDER BY 1", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<string>();
        while (await reader.ReadAsync())
            ids.Add(reader.GetString(0));

        return ids;
    }

    private static async Task<T?> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    private static PostgresException FindPostgresException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is PostgresException postgres)
                return postgres;

            if (current.InnerException is null)
                break;
        }

        throw new InvalidOperationException($"No PostgresException in the chain of {exception.GetType().Name}.");
    }

    /// <summary>Holds the migration advisory lock of <typeparamref name="TContext"/> from an outside session.</summary>
    private static async Task<NpgsqlConnection> HoldMigrationLockAsync<TContext>(PostgresTestDatabase db)
    {
        var key = AdvisoryLockKeys.ToKey(AdvisoryLockKeys.Migration(typeof(TContext).FullName!));
        var connection = new NpgsqlConnection(db.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT pg_advisory_lock({key})", connection);
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The awaited condition never held.");

            await Task.Delay(100);
        }
    }

    // ---- T-02: the real role split ----

    [Fact]
    public async Task RoleSplit_TheMigratorCreatesTheSchema_AndTheRuntimeRoleOnlyReadsAndWritesIt()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await using var host = PackageHost<PkgSplitContext>(db, "__ef_pkg_split");

        await host.StartAsync();

        host.Startup.IsCompleted.Should().BeTrue();
        (await HistoryAsync(db, "__ef_pkg_split")).Should().Equal("20260101000000_PkgSplitInit");
        (await ScalarAsync<string>(db.AdminConnectionString, "SELECT tableowner FROM pg_tables WHERE tablename = 'pkg_split_things'"))
            .Should().Be(PostgresTestRoles.Migrator, "the table is created over the migration data source");

        var id = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(db.RuntimeConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand("INSERT INTO pkg_split_things (id, name) VALUES ($1, 'x')", connection);
            insert.Parameters.Add(new NpgsqlParameter { Value = id });
            (await insert.ExecuteNonQueryAsync()).Should().Be(1, "the default privileges of the migrator reach the runtime role");

            await using var select = new NpgsqlCommand("SELECT count(*) FROM pkg_split_things", connection);
            ((long)(await select.ExecuteScalarAsync())!).Should().Be(1);

            await using var create = new NpgsqlCommand("CREATE TABLE runtime_made (id int)", connection);
            var denied = await FluentActions.Awaiting(() => create.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>();
            denied.Which.SqlState.Should().Be("42501", "the runtime role cannot create objects");
        }
    }

    // ---- T-03: never on the runtime connection ----

    [Fact]
    public async Task WithTheMigrationDataSource_TheDdlRunsAsTheMigrator_NeverAsTheRuntimeRole()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await using var host = PackageHost<PkgSplitContext>(db, "__ef_pkg_split");

        await host.StartAsync();

        host.Ddl.DdlUsers.Should().NotBeEmpty();
        host.Ddl.DdlUsers.Should().OnlyContain(user => user == PostgresTestRoles.Migrator);
    }

    [Fact]
    public async Task WithoutTheMigrationDataSource_TheRuntimeRoleIsUsed_AndFailsStartWithThePermissionError()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await using var host = PackageHost<PkgSplitContext>(db, "__ef_pkg_split", withMigrator: false);

        var failure = await FluentActions.Awaiting(() => host.StartAsync()).Should().ThrowAsync<Exception>();

        FindPostgresException(failure.Which).SqlState.Should().Be("42501");
        host.Ddl.DdlUsers.Should().OnlyContain(user => user == PostgresTestRoles.Runtime, "the fallback is the context's own connection");
        host.Startup.IsCompleted.Should().BeFalse();
        await FluentActions.Awaiting(() => host.Startup.WaitAsync()).Should().ThrowAsync<Exception>();
        (await ScalarAsync<string>(db.AdminConnectionString, "SELECT to_regclass('pkg_split_things')::text")).Should().BeNull();
    }

    // ---- T-04: cross-replica serialization ----


    [Fact]
    public async Task TwoHostsStartingTogether_RunTheMigrationOnce_AndTakeTheLockOneAfterTheOther()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        var logs = new RecordingLoggerProvider();
        await using var first = PackageHost<PkgConcurrentContext>(db, "__ef_pkg_concurrent", more: s => s.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(logs)));
        await using var second = PackageHost<PkgConcurrentContext>(db, "__ef_pkg_concurrent", more: s => s.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(logs)));

        await Task.WhenAll(first.StartAsync(), second.StartAsync());

        (first.Ddl.Count("pkg_concurrent_things") + second.Ddl.Count("pkg_concurrent_things")).Should().Be(1, "the replica that waited finds the migration applied");
        first.Startup.IsCompleted.Should().BeTrue();
        second.Startup.IsCompleted.Should().BeTrue();
        (await HistoryAsync(db, "__ef_pkg_concurrent")).Should().ContainSingle();

        var lockEvents = logs.EventIds
            .Where(id => id is EfCoreLoggingEventIds.AdvisoryLockAcquired or EfCoreLoggingEventIds.AdvisoryLockReleased)
            .ToArray();
        lockEvents.Should().Equal(
            EfCoreLoggingEventIds.AdvisoryLockAcquired,
            EfCoreLoggingEventIds.AdvisoryLockReleased,
            EfCoreLoggingEventIds.AdvisoryLockAcquired,
            EfCoreLoggingEventIds.AdvisoryLockReleased);
    }

    [Fact]
    public async Task AHeldLock_KeepsThePersistenceStartupPending_UntilItIsReleased()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await using var host = PackageHost<PkgLockedContext>(db, "__ef_pkg_locked");
        await using var outsider = await HoldMigrationLockAsync<PkgLockedContext>(db);

        var start = host.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        start.IsCompleted.Should().BeFalse("another replica holds the lock");
        host.Startup.IsCompleted.Should().BeFalse();

        await using (var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({AdvisoryLockKeys.ToKey(AdvisoryLockKeys.Migration(typeof(PkgLockedContext).FullName!))})", outsider))
            await release.ExecuteNonQueryAsync();

        await start.WaitAsync(Patience);
        host.Startup.IsCompleted.Should().BeTrue();
        (await HistoryAsync(db, "__ef_pkg_locked")).Should().ContainSingle();
    }

    [Fact]
    public async Task AShortLockTimeout_FailsStartWithATimeout_AndNothingIsMigrated()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await using var host = PackageHost<PkgLockedContext>(db, "__ef_pkg_locked", lockTimeout: TimeSpan.FromMilliseconds(600));
        await using var outsider = await HoldMigrationLockAsync<PkgLockedContext>(db);

        await FluentActions.Awaiting(() => host.StartAsync()).Should().ThrowAsync<TimeoutException>();

        host.Startup.IsCompleted.Should().BeFalse();
        await FluentActions.Awaiting(() => host.Startup.WaitAsync()).Should().ThrowAsync<TimeoutException>();
        (await ScalarAsync<string>(db.AdminConnectionString, "SELECT to_regclass('pkg_locked_things')::text")).Should().BeNull();
    }

    // ---- T-05: coexistence with the service's own startup migration ----

    private Host CoexistHost(
        PostgresTestDatabase db,
        string packageHistoryTable,
        bool includePackageMigration = true)
    {
        var logs = new RecordingLoggerProvider();
        var ddl = new DdlRecorder();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:svc"] = db.RuntimeConnectionString,
                ["SharedKernel:Persistence:svc:MigrationConnectionString"] = db.MigratorConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
        services.AddTestEntityVersionKeys();
        services.AddSharedKernelPostgres<SvcCoexistContext>(configuration, "svc", p => p
            .ConfigureDbContext((_, o) => Quiet(o))
            .MigrateOnStartup());
        services.AddDbContext<PkgCoexistContext>((sp, o) =>
        {
            o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>(), n => n.MigrationsHistoryTable(packageHistoryTable));
            o.AddInterceptors(ddl);
            Quiet(o);
        });
        if (includePackageMigration)
            services.AddMigrationOnStartup<PkgCoexistContext>();

        return new Host(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), logs, ddl);
    }

    [Fact]
    public async Task AServiceContextAndAPackageContext_MigrateSideBySide_WithSeparateHistoryTables()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await using var host = CoexistHost(db, "__ef_pkg_coexist");

        await host.StartAsync();

        host.Startup.IsCompleted.Should().BeTrue();
        (await HistoryAsync(db, "__EFMigrationsHistory")).Should().Equal("20260101000000_SvcCoexistInit");
        (await HistoryAsync(db, "__ef_pkg_coexist")).Should().Equal("20260101000000_PkgCoexistInit");
        (await ScalarAsync<string>(db.AdminConnectionString, "SELECT to_regclass('svc_coexist_things')::text")).Should().NotBeNull();
        (await ScalarAsync<string>(db.AdminConnectionString, "SELECT to_regclass('pkg_coexist_things')::text")).Should().NotBeNull();
    }

    [Fact]
    public async Task ReadinessWaitsForBothContexts_WhileThePackageMigrationIsBlocked()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await using var host = CoexistHost(db, "__ef_pkg_coexist");
        await using var outsider = await HoldMigrationLockAsync<PkgCoexistContext>(db);

        var start = host.StartAsync();
        await WaitUntilAsync(async () =>
            await ScalarAsync<string>(db.AdminConnectionString, "SELECT to_regclass('svc_coexist_things')::text") is not null);

        host.Startup.IsCompleted.Should().BeFalse("the service context finished but the package context still waits");
        start.IsCompleted.Should().BeFalse();

        await using (var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({AdvisoryLockKeys.ToKey(AdvisoryLockKeys.Migration(typeof(PkgCoexistContext).FullName!))})", outsider))
            await release.ExecuteNonQueryAsync();

        await start.WaitAsync(Patience);
        host.Startup.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task APackageContextLeftOnTheDefaultHistoryTable_FailsStart_BeforeAnythingIsMigrated()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await using var host = CoexistHost(db, packageHistoryTable: HistoryRepository.DefaultTableName);

        var failure = await FluentActions.Awaiting(() => host.StartAsync()).Should().ThrowAsync<OptionsValidationException>();

        failure.Which.Message.Should().Contain("default migrations history table");
        (await ScalarAsync<string>(db.AdminConnectionString, "SELECT to_regclass('svc_coexist_things')::text")).Should().BeNull(
            "start-time validation runs before any hosted service");
    }

    [Fact]
    public async Task APackageContextOnTheDefaultHistoryTable_WithoutTheRegistrationCall_IsNotValidated()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await using var host = CoexistHost(db, HistoryRepository.DefaultTableName, includePackageMigration: false);

        await host.StartAsync();

        host.Startup.IsCompleted.Should().BeTrue();
        (await ScalarAsync<string>(db.AdminConnectionString, "SELECT to_regclass('pkg_coexist_things')::text")).Should().BeNull(
            "the package context is only migrated when the method is called");
    }

    // ---- T-06: a hand-created schema and no call keeps behaving as before ----

    [Fact]
    public async Task ASchemaCreatedByHand_WithTheMethodNeverCalled_StartsAsBeforeAndIsUntouched()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await db.ExecuteAsMigratorAsync("CREATE TABLE pkg_hand_things (id uuid PRIMARY KEY); INSERT INTO pkg_hand_things VALUES (gen_random_uuid());");
        var ddl = new DdlRecorder();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:svc"] = db.RuntimeConnectionString,
                ["SharedKernel:Persistence:svc:MigrationConnectionString"] = db.MigratorConnectionString,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTestEntityVersionKeys();
        services.AddSharedKernelPostgres<SvcCoexistContext>(configuration, "svc", p => p.ConfigureDbContext((_, o) => Quiet(o)));
        services.AddDbContext<PkgHandContext>((sp, o) =>
        {
            o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>(), n => n.MigrationsHistoryTable("__ef_pkg_hand"));
            o.AddInterceptors(ddl);
            Quiet(o);
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var host = new Host(provider, new RecordingLoggerProvider(), ddl);

        await host.StartAsync();

        provider.GetServices<IHostedService>().OfType<ContextMigrationHostedService<PkgHandContext>>().Should().BeEmpty();
        host.Startup.IsCompleted.Should().BeTrue("nothing runs at startup");
        (await ScalarAsync<string>(db.AdminConnectionString, "SELECT to_regclass('\"__ef_pkg_hand\"')::text")).Should().BeNull();
        (await ScalarAsync<long>(db.AdminConnectionString, "SELECT count(*) FROM pkg_hand_things")).Should().Be(1);
        ddl.DdlUsers.Should().BeEmpty();
    }
}
