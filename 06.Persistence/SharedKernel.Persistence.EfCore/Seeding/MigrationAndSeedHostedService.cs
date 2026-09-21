using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.MultiTenancy;

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
/// deadlock. Behind a transaction-mode pooler, point the migration at a direct connection.
/// </para>
/// <para>
/// <strong>Seeders</strong> act as a system caller named <c>seeder:{Type}</c> (so audit columns show which
/// seeder wrote a row). On a <see cref="TenantedDbContext"/> a seeder runs inside a cross-tenant scope entered
/// with that caller: writes to any tenant pass the tenant write guard; reads still see the tenant filter, so a
/// seeder that checks for existing rows queries with <c>IgnoreQueryFilters([PersistenceFilterNames.Tenant])</c>.
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
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
                await using var context = await factory.CreateDbContextAsync(cancellationToken);
                await context.Database.MigrateAsync(cancellationToken);
            }

            foreach (var (seederTypeName, invoke) in _seedSteps)
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
                await using var context = await factory.CreateDbContextAsync(cancellationToken);

                var seeder = new SystemRequestContext([], $"seeder:{seederTypeName}");
                context.RefreshRequestContext(seeder);

                if (context is TenantedDbContext)
                {
                    var crossTenantScope = new CrossTenantScope(
                        seeder, scope.ServiceProvider.GetService<ILogger<CrossTenantScope>>());
                    using (crossTenantScope.Enter($"startup seeder {seederTypeName} for {contextTypeName}"))
                        await invoke(scope.ServiceProvider, context, cancellationToken);
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

    /// <summary>What the startup sequence does.</summary>
    internal sealed record StartupOptions(bool MigrateOnStartup, TimeSpan LockTimeout);
}
