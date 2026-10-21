using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Seeding;

namespace SharedKernel.Persistence;

public static partial class PostgresPersistenceExtensions
{
    /// <summary>
    /// Applies the pending migrations of a <see cref="DbContext"/> a kernel package owns (for example the idempotency
    /// store's context) when the host starts, on the migration data source and under the cross-replica migration lock.
    /// </summary>
    /// <typeparam name="TContext">
    /// A plain, non-pooled context registered with <c>AddDbContext</c> that is not a <see cref="SharedKernelDbContext"/>
    /// (use <c>MigrateOnStartup()</c> for those) and that has a migrations history table of its own.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lockTimeout">
    /// How long startup waits for the cross-replica lock, which is keyed by the context's full type name.
    /// Default 2 minutes; when it elapses, startup fails with a <see cref="TimeoutException"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/>.</returns>
    /// <remarks>
    /// Migrations run over the <c>NpgsqlDataSourceKeys.Migration</c> data source when <c>MigrationConnectionString</c> is
    /// configured, else over the context's own connection. <c>IPersistenceStartup</c> (and so readiness) completes only
    /// after this migration. A lock timeout or a failed migration fails host start. Registering the same context twice,
    /// a <see cref="SharedKernelDbContext"/> or a context on the default history table is rejected.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The context is already registered or derives <see cref="SharedKernelDbContext"/>.</exception>
    public static IServiceCollection AddMigrationOnStartup<TContext>(
        this IServiceCollection services,
        TimeSpan? lockTimeout = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        if (lockTimeout is { } timeout)
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero, nameof(lockTimeout));

        if (typeof(SharedKernelDbContext).IsAssignableFrom(typeof(TContext)))
        {
            throw new InvalidOperationException(
                $"'{typeof(TContext).Name}' derives SharedKernelDbContext and migrates through MigrateOnStartup() on AddSharedKernelPostgres.");
        }

        if (services.Any(sd => sd.ServiceType == typeof(PackageMigrationMarker<TContext>)))
        {
            throw new InvalidOperationException(
                $"'{typeof(TContext).Name}' is already registered for migration on startup. Call AddMigrationOnStartup<{typeof(TContext).Name}> once.");
        }

        services.AddSingleton<PackageMigrationMarker<TContext>>();

        var effectiveTimeout = lockTimeout ?? TimeSpan.FromMinutes(2);
        var startupSignal = PersistenceStartupSignalRegistration.GetOrAdd(services);
        startupSignal.Expect(typeof(TContext));

        // Validation first, so a rejected context fails start before any migration runs.
        services.AddOptions<PackageContextMigrationCheck<TContext>>().ValidateOnStart();
        services.AddSingleton<IValidateOptions<PackageContextMigrationCheck<TContext>>, PackageContextMigrationValidator<TContext>>();

        services.AddHostedService(sp => new ContextMigrationHostedService<TContext>(
            sp, effectiveTimeout, sp.GetRequiredService<ILogger<ContextMigrationHostedService<TContext>>>(), startupSignal));

        return services;
    }

    private sealed class PackageMigrationMarker<TContext>
        where TContext : DbContext;
}
