using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SharedKernel.Application.Context;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.Npgsql.Connections;

namespace SharedKernel.Persistence.EfCore.Seeding;

/// <summary>
/// Applies pending migrations and runs the registered seeders at startup, serialized across replicas.
/// Registered only when <c>MigrateOnStartup()</c> or <c>AddSeeder&lt;T&gt;()</c> was called.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Locking.</strong> The whole sequence runs under the registered <see cref="IMigrationLock"/> (the
/// Npgsql advisory lock), waiting up to <see cref="StartupOptions.LockTimeout"/> (default 2 minutes, set with
/// <c>UseStartupLockTimeout</c>). Without a lock the sequence still runs and an Error is logged. Since EF Core 9,
/// <c>Database.MigrateAsync</c> also takes its own database lock (Npgsql: an advisory lock held for the
/// migration), so two replicas never apply the same migration twice even without this lock; what this lock adds
/// is that seeders never run against a half-migrated schema and never run concurrently with each other. The two
/// locks use different keys and are taken in the same order by every replica (ours first), so they cannot
/// deadlock. Behind a transaction-mode pooler, set <c>MigrationConnectionString</c> to a direct connection: when
/// that migration data source is registered, <c>MigrateAsync</c> runs over it.
/// </para>
/// <para>
/// <strong>Seeders</strong> act as a system caller named <c>seeder:{Type}</c> (so audit columns show which
/// seeder wrote a row). On a <see cref="TenantedDbContext"/> a seeder runs inside a cross-tenant scope entered
/// with that caller: writes to any tenant pass the tenant write guard; reads still see the tenant filter, so a
/// seeder that checks for existing rows queries with <c>IgnoreQueryFilters([PersistenceFilterNames.Tenant])</c>.
/// With row-level security the seeder's context runs on the cross-tenant connection
/// (<c>UseCrossTenantConnection()</c>), so the cross-tenant connection string must be configured.
/// </para>
/// </remarks>
internal sealed class MigrationAndSeedHostedService<TContext> : IHostedService
    where TContext : SharedKernelDbContext
{
    private readonly IServiceProvider _serviceProvider;
    private readonly StartupOptions _options;
    private readonly IReadOnlyList<(string SeederTypeName, Func<IServiceProvider, TContext, CancellationToken, Task> Invoke)> _seedSteps;
    private readonly ILogger _logger;

    public MigrationAndSeedHostedService(
        IServiceProvider serviceProvider,
        StartupOptions options,
        IReadOnlyList<(string SeederTypeName, Func<IServiceProvider, TContext, CancellationToken, Task> Invoke)> seedSteps,
        ILogger<MigrationAndSeedHostedService<TContext>>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _seedSteps = seedSteps;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var contextTypeName = typeof(TContext).Name;
        var lockKey = typeof(TContext).FullName ?? typeof(TContext).Name;
        var migrationLock = _serviceProvider.GetService<IMigrationLock>();

        IAsyncDisposable? lockHandle = null;

        PersistenceLog.MigrationAndSeedStarted(_logger, contextTypeName);

        try
        {
            if (migrationLock is not null)
            {
                lockHandle = await migrationLock.AcquireAsync(lockKey, _options.LockTimeout, cancellationToken);
                PersistenceLog.AdvisoryLockAcquired(_logger, contextTypeName);
            }
            else
            {
                PersistenceLog.NoMigrationLockRegistered(_logger, contextTypeName);
            }

            if (_options.MigrateOnStartup)
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var migrationDataSource = scope.ServiceProvider.GetKeyedService<NpgsqlDataSource>(NpgsqlDataSourceKeys.Migration);
                await using var context = await CreateContextAsync(
                    scope.ServiceProvider, AnonymousRequestContext.Instance, dedicated: migrationDataSource is not null, cancellationToken);

                // Behind a transaction-mode pooler the migration needs a direct connection (one server session for
                // EF Core's migration lock and the DDL); the configured migration data source provides it.
                if (migrationDataSource is not null)
                    context.Database.SetDbConnection(migrationDataSource.CreateConnection(), contextOwnsConnection: true);

                await context.Database.MigrateAsync(cancellationToken);
            }

            foreach (var (seederTypeName, invoke) in _seedSteps)
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var seeder = new SystemRequestContext([], $"seeder:{seederTypeName}");
                var tenanted = typeof(TenantedDbContext).IsAssignableFrom(typeof(TContext));
                var rowLevelSecurity = tenanted
                    && scope.ServiceProvider.GetService<RowLevelSecurityCommandInterceptor>() is not null;

                await using var context = await CreateContextAsync(
                    scope.ServiceProvider, seeder, dedicated: rowLevelSecurity, cancellationToken);

                if (tenanted)
                {
                    var crossTenantScope = new CrossTenantScope(
                        seeder, scope.ServiceProvider.GetService<ILogger<CrossTenantScope>>());
                    using (crossTenantScope.Enter($"startup seeder {seederTypeName} for {contextTypeName}"))
                    {
                        // Under row-level security the application role only sees the bound tenant: a seeder that
                        // writes across tenants runs on the cross-tenant role's connection.
                        if (rowLevelSecurity)
                            context.Database.UseCrossTenantConnection();

                        await invoke(scope.ServiceProvider, context, cancellationToken);
                    }
                }
                else
                {
                    await invoke(scope.ServiceProvider, context, cancellationToken);
                }

                PersistenceLog.SeederApplied(_logger, seederTypeName, contextTypeName);
            }

            PersistenceLog.MigrationAndSeedCompleted(_logger, contextTypeName);
        }
        catch (Exception ex)
        {
            PersistenceLog.MigrationAndSeedFailed(_logger, ex, contextTypeName);
            throw;
        }
        finally
        {
            if (lockHandle is not null)
            {
                await lockHandle.DisposeAsync();
                PersistenceLog.AdvisoryLockReleased(_logger, contextTypeName);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // A context for one startup step, acting as caller. When the step swaps the connection (migration data source,
    // cross-tenant connection) the context must not come from the DbContext pool — the swapped connection would
    // outlive the lease — so a dedicated, unpooled copy with the same (already complete, frozen) options is built.
    private static async Task<TContext> CreateContextAsync(
        IServiceProvider services, IRequestContext caller, bool dedicated, CancellationToken cancellationToken)
    {
        var dispatcher = services.GetService<IDomainEventDispatcher>();
        var leased = await services.GetRequiredService<ICallerDbContextFactory<TContext>>()
            .CreateDbContextAsync(caller, dispatcher, cancellationToken);

        var options = (DbContextOptions<TContext>)leased.GetService<IDbContextOptions>();
        if (!dedicated || options.FindExtension<CoreOptionsExtension>()?.MaxPoolSize is null)
            return leased;

        await leased.DisposeAsync();

        var builder = new DbContextOptionsBuilder<TContext>(options);
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
            options.FindExtension<CoreOptionsExtension>()!.WithMaxPoolSize(null));
        var unpooled = builder.Options;
        unpooled.Freeze(); // the platform interceptors are already in the options; OnConfiguring must not add them twice

        var context = ActivatorUtilities.CreateInstance<TContext>(services, unpooled);
        context.AttachLease(caller, dispatcher);
        return context;
    }

    /// <summary>What the startup sequence does.</summary>
    internal sealed record StartupOptions(bool MigrateOnStartup, TimeSpan LockTimeout);
}
