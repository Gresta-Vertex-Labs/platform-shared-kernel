using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Context;

namespace SharedKernel.Persistence.EfCore.Seeding;

/// <summary>
/// Startup orchestration that applies pending EF Core migrations and runs registered
/// <see cref="IDataSeeder{TContext}"/> implementations for <typeparamref name="TContext"/>,
/// guarded by a PostgreSQL advisory lock so multiple replicas racing on startup serialize to a
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
///   <item><description>
///   If an <see cref="IDbConnectionFactory"/> is registered, acquire a PostgreSQL advisory lock via
///   <c>pg_advisory_lock(hashtext(lockKey))</c>, where <c>lockKey</c> is
///   <typeparamref name="TContext"/>'s full type name. If no <see cref="IDbConnectionFactory"/> is
///   registered (e.g., a non-PostgreSQL provider), the lock step is skipped — migrations/seeding
///   proceed without cross-replica coordination.
///   </description></item>
///   <item><description>
///   If migrations-on-startup was requested, resolve <see cref="IDbContextFactory{TContext}"/> and
///   call <c>Database.MigrateAsync</c>.
///   </description></item>
///   <item><description>
///   Run each registered seeder in registration order, each resolved in its own DI scope with its
///   own <typeparamref name="TContext"/> instance via <see cref="IDbContextFactory{TContext}"/>.
///   </description></item>
///   <item><description>
///   Release the advisory lock via <c>pg_advisory_unlock</c> in a <c>finally</c> block.
///   </description></item>
/// </list>
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
    private readonly IServiceProvider _serviceProvider;
    private readonly bool _runMigrations;
    private readonly IReadOnlyList<Func<IServiceProvider, TContext, CancellationToken, Task>> _seedSteps;

    /// <summary>
    /// Initialises a new <see cref="MigrationAndSeedHostedService{TContext}"/>.
    /// </summary>
    /// <param name="serviceProvider">The root service provider, used to create per-step DI scopes.</param>
    /// <param name="runMigrations">Whether to call <c>Database.MigrateAsync</c> on startup.</param>
    /// <param name="seedSteps">
    /// Closed-generic delegates, one per registered <see cref="IDataSeeder{TContext}"/>, each
    /// resolving its seeder from the supplied scope's <see cref="IServiceProvider"/> and calling
    /// <see cref="IDataSeeder{TContext}.SeedAsync"/> with the supplied context — built at
    /// <c>.AddSeeder&lt;TSeeder&gt;()</c> call time so no reflection is needed here.
    /// </param>
    public MigrationAndSeedHostedService(
        IServiceProvider serviceProvider,
        bool runMigrations,
        IReadOnlyList<Func<IServiceProvider, TContext, CancellationToken, Task>> seedSteps)
    {
        _serviceProvider = serviceProvider;
        _runMigrations = runMigrations;
        _seedSteps = seedSteps;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var lockKey = typeof(TContext).FullName ?? typeof(TContext).Name;
        var connectionFactory = _serviceProvider.GetService<IDbConnectionFactory>();

        System.Data.IDbConnection? lockConnection = null;

        try
        {
            if (connectionFactory is not null)
            {
                lockConnection = await connectionFactory.CreateConnectionAsync(cancellationToken);
                using var lockCommand = lockConnection.CreateCommand();
                lockCommand.CommandText = "SELECT pg_advisory_lock(hashtext(@lockKey))";
                AddParameter(lockCommand, "lockKey", lockKey);
                lockCommand.ExecuteNonQuery();
            }

            if (_runMigrations)
            {
                var contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<TContext>>();
                await using var migrationContext = await contextFactory.CreateDbContextAsync(cancellationToken);
                await migrationContext.Database.MigrateAsync(cancellationToken);
            }

            foreach (var seedStep in _seedSteps)
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
                await using var seedContext = await contextFactory.CreateDbContextAsync(cancellationToken);

                await seedStep(scope.ServiceProvider, seedContext, cancellationToken);
            }
        }
        finally
        {
            if (lockConnection is not null)
            {
                try
                {
                    using var unlockCommand = lockConnection.CreateCommand();
                    unlockCommand.CommandText = "SELECT pg_advisory_unlock(hashtext(@lockKey))";
                    AddParameter(unlockCommand, "lockKey", lockKey);
                    unlockCommand.ExecuteNonQuery();
                }
                finally
                {
                    lockConnection.Dispose();
                }
            }
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void AddParameter(System.Data.IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
