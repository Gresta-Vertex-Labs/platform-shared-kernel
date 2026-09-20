using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Seeding;

/// <summary>
/// Startup orchestration that applies pending EF Core migrations and runs registered
/// <see cref="IDataSeeder{TContext}"/> implementations for <typeparamref name="TContext"/>,
/// guarded by an <see cref="IMigrationLock"/> so multiple replicas racing on startup serialize to a
/// single instance.
/// </summary>
/// <typeparam name="TContext">The <see cref="SharedKernelDbContext"/> subclass to migrate/seed.</typeparam>
/// <remarks>
/// <para>
/// Registered by <c>EfCorePersistenceBuilder&lt;TContext&gt;.Build()</c> only when
/// <c>.WithMigrationsOnStartup()</c> was called, or at least one seeder was registered via
/// <c>.AddSeeder&lt;TSeeder&gt;()</c>.
/// </para>
/// <para>
/// <strong>StartAsync sequence:</strong>
/// <list type="number">
/// <item><description>
/// If an <see cref="IMigrationLock"/> is registered, acquire it (named after
/// <typeparamref name="TContext"/>'s full type name) before doing anything else. If none is
/// registered, log an Error-level warning — startup coordination across replicas
/// is NOT guaranteed in that configuration — and proceed without a lock, rather than crash-looping
/// a deliberately single-replica or non-PostgreSQL deployment.
/// </description></item>
/// <item><description>
/// If migrations-on-startup was requested, resolve <see cref="IDbContextFactory{TContext}"/> and
/// call <c>Database.MigrateAsync</c>.
/// </description></item>
/// <item><description>
/// Run each registered seeder in registration order, each resolved in its own DI scope with its
/// own <typeparamref name="TContext"/> instance via <see cref="IDbContextFactory{TContext}"/>.
/// </description></item>
/// <item><description>
/// Release the lock (if acquired) by disposing its handle in a <c>finally</c> block.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// <strong></strong> no longer issues raw PostgreSQL <c>pg_advisory_lock</c> SQL
/// directly — a hard violation of this package's "never provider-specific SQL" rule. The concrete
/// PostgreSQL implementation (<c>NpgsqlAdvisoryMigrationLock</c>) lives in
/// <c>SharedKernel.Persistence.Npgsql</c>. See <see cref="IMigrationLock"/>'s remarks for the full
/// rationale, including the StartupGate/liveness-probe integration guidance.
/// </para>
/// <para>
/// <see cref="StopAsync"/> is a no-op.
/// </para>
/// <para>
/// Non-goals: this is not a migration-authoring tool (use <c>dotnet ef migrations add</c> as
/// normal) and does not replace <c>.WithCompiledModel()</c> — compiled models and migrations
/// operate independently.
/// </para>
/// </remarks>
internal sealed class MigrationAndSeedHostedService<TContext> : IHostedService
    where TContext : SharedKernelDbContext
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(2);

    private readonly IServiceProvider _serviceProvider;
    private readonly bool _runMigrations;
    private readonly IReadOnlyList<(string SeederTypeName, Func<IServiceProvider, TContext, CancellationToken, Task> Invoke)> _seedSteps;
    private readonly ILogger<MigrationAndSeedHostedService<TContext>> _logger;

    /// <summary>
    /// Initialises a new <see cref="MigrationAndSeedHostedService{TContext}"/>.
    /// </summary>
    /// <param name="serviceProvider">The root service provider, used to create per-step DI scopes.</param>
    /// <param name="runMigrations">Whether to call <c>Database.MigrateAsync</c> on startup.</param>
    /// <param name="seedSteps">
    /// One entry per registered <see cref="IDataSeeder{TContext}"/> — the seeder's own type name
    /// (captured at <c>.AddSeeder&lt;TSeeder&gt;()</c> call time, for the <c>SeederApplied</c> log)
    /// paired with a closed-generic delegate resolving its seeder from the supplied
    /// scope's <see cref="IServiceProvider"/> and calling <see cref="IDataSeeder{TContext}.SeedAsync"/>
    /// with the supplied context — no reflection is needed here.
    /// </param>
    /// <param name="logger">
    /// Optional logger for the lifecycle Information/Warning/Error logs (EventIds <c>6001</c>-<c>6006</c>,
    /// <c>6009</c>). Resolved by DI when registered; falls back to
    /// <see cref="NullLogger{T}"/> otherwise.
    /// </param>
    public MigrationAndSeedHostedService(
        IServiceProvider serviceProvider,
        bool runMigrations,
        IReadOnlyList<(string SeederTypeName, Func<IServiceProvider, TContext, CancellationToken, Task> Invoke)> seedSteps,
        ILogger<MigrationAndSeedHostedService<TContext>>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _runMigrations = runMigrations;
        _seedSteps = seedSteps;
        _logger = logger ?? NullLogger<MigrationAndSeedHostedService<TContext>>.Instance;
    }

    /// <inheritdoc />
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
                lockHandle = await migrationLock.AcquireAsync(lockKey, LockTimeout, cancellationToken);
                PersistenceLog.AdvisoryLockAcquired(_logger, contextTypeName);
            }
            else
            {
                PersistenceLog.NoMigrationLockRegistered(_logger, contextTypeName);
            }

            if (_runMigrations)
            {
                // IDbContextFactory<TContext> is now registered SCOPED (it attaches the
                // calling scope's actor/tenant identity — see TenantAwareDbContextFactory<TContext>),
                // so it can no longer be resolved directly from the root _serviceProvider. A
                // migration run has no meaningful tenant identity of its own — a fresh, empty scope
                // gets the builder's fail-closed defaults, which is correct: schema migration touches
                // no tenant-scoped rows.
                await using var migrationScope = _serviceProvider.CreateAsyncScope();
                var contextFactory = migrationScope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
                await using var migrationContext = await contextFactory.CreateDbContextAsync(cancellationToken);
                await migrationContext.Database.MigrateAsync(cancellationToken);
            }

            foreach (var (seederTypeName, invoke) in _seedSteps)
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
                await using var seedContext = await contextFactory.CreateDbContextAsync(cancellationToken);

                await invoke(scope.ServiceProvider, seedContext, cancellationToken);

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
                // Release MUST use CancellationToken.None — an already-cancelled StartAsync token
                // must never skip releasing a lock this instance successfully acquired.
                await lockHandle.DisposeAsync();
                PersistenceLog.AdvisoryLockReleased(_logger, contextTypeName);
            }
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
