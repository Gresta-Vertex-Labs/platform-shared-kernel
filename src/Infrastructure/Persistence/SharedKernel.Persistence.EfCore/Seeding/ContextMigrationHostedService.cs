using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SharedKernel.Persistence.Npgsql.Connections;

namespace SharedKernel.Persistence.EfCore.Seeding;

/// <summary>
/// Applies the pending migrations of a context a kernel package owns, serialized across replicas by the same
/// <see cref="Abstractions.Coordination.IMigrationLock"/> as <see cref="MigrationAndSeedHostedService{TContext}"/>.
/// Registered by <c>AddMigrationOnStartup&lt;TContext&gt;()</c>. Migrations only: no seeders, no cross-tenant scope.
/// </summary>
/// <remarks>
/// The migration runs over the <see cref="NpgsqlDataSourceKeys.Migration"/> data source when one is registered
/// (the privileged migrator role), else over the context's own connection.
/// </remarks>
internal sealed class ContextMigrationHostedService<TContext>(
    IServiceProvider serviceProvider,
    TimeSpan lockTimeout,
    ILogger<ContextMigrationHostedService<TContext>> logger,
    PersistenceStartupSignal? startupSignal)
    : IHostedService
    where TContext : DbContext
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        StartupSequenceRunner.RunAsync(
            serviceProvider, typeof(TContext), lockTimeout, logger, startupSignal, MigrateAsync, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var migrationDataSource = scope.ServiceProvider.GetKeyedService<NpgsqlDataSource>(NpgsqlDataSourceKeys.Migration);
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        if (migrationDataSource is not null)
            context.Database.SetDbConnection(migrationDataSource.CreateConnection(), contextOwnsConnection: true);

        await context.Database.MigrateAsync(cancellationToken);
    }
}
