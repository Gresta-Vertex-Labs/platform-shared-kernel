using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Execution.Context;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.EfCore.Context;
using SharedKernel.Idempotency.EfCore.Diagnostics;
using SharedKernel.Idempotency.EfCore.Internal;
using SharedKernel.Idempotency.EfCore.Options;
using SharedKernel.Idempotency.EfCore.Store;
using SharedKernel.Persistence;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Idempotency.EfCore.Extensions;

/// <summary><see cref="IServiceCollection"/> extension methods for registering the PostgreSQL idempotency store.</summary>
public static class EfCoreIdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IdempotencyDbContext"/> and <see cref="EfCoreIdempotencyStore"/> as the
    /// <see cref="IIdempotencyStore"/> for every purpose <paramref name="purposes"/> selects, keyed by purpose.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureDbContext">
    /// Configures the context — typically <c>options.UsePostgres(dataSource)</c> from <c>SharedKernel.Persistence.EfCore</c>.
    /// </param>
    /// <param name="purposes">Selects the purposes, for example <c>p =&gt; p.ForRequests().ForMessages()</c>.</param>
    /// <param name="configureOptions">Optional delegate to customise <see cref="EfCoreIdempotencyOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No purpose was selected, or a store is already registered for a selected purpose.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Requires an <see cref="IClock"/> (e.g. <c>services.AddClock()</c>): every reservation and expiry timestamp comes
    /// from it. Registers <see cref="IRequestContextAccessor"/> unless one is already registered: every key is scoped
    /// by the tenant of the ambient request context, which the service's inbound adapters set.
    /// </para>
    /// <para>
    /// Also calls <c>AddMetrics()</c> and registers the singleton instruments of the <c>SharedKernel.Idempotency</c>
    /// meter, so a host that never configured metrics still resolves the store.
    /// </para>
    /// <para>
    /// <paramref name="configureOptions"/> runs once, here, not when the options are first resolved. With
    /// <see cref="EfCoreIdempotencyOptions.MigrateOnStartup"/> the package creates its table through its shipped
    /// migration on the migration connection (see <c>README.md</c>); otherwise it creates nothing. Neither path runs
    /// a cleanup: <c>README.md</c> carries the job that bounds table growth.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddEfCoreIdempotency(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDbContext,
        Action<IdempotencyPurposeSelection> purposes,
        Action<EfCoreIdempotencyOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureDbContext);
        ArgumentNullException.ThrowIfNull(purposes);

        var selected = IdempotencyServiceCollectionExtensions.SelectPurposes(purposes);

        // The migration flag decides what is registered, so the delegate runs once, here, on a snapshot; the same
        // values then back the IOptions registration (the delegate never runs a second time).
        var snapshot = new EfCoreIdempotencyOptions();
        configureOptions?.Invoke(snapshot);
        if (snapshot.MigrationLockTimeout is { } lockTimeout)
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lockTimeout, TimeSpan.Zero, nameof(configureOptions));

        var migrateOnStartup = snapshot.MigrateOnStartup;

        services.AddDbContext<IdempotencyDbContext>(options =>
        {
            configureDbContext(options);
            IdempotencyDbContextOptions.DisableRetry(options);

            // A 1.0.0 consumer may run its own migration for this context against EF's default history table, so the
            // table of its own is applied only when the package migrates the context.
            if (migrateOnStartup)
                IdempotencyDbContextOptions.UseHistoryTable(options);
        });

        services
            .AddOptions<EfCoreIdempotencyOptions>()
            .Configure(o =>
            {
                o.AllowExecutionOnStoreUnavailable = snapshot.AllowExecutionOnStoreUnavailable;
                o.MigrateOnStartup = snapshot.MigrateOnStartup;
                o.MigrationLockTimeout = snapshot.MigrationLockTimeout;
            });

        if (migrateOnStartup)
            services.AddMigrationOnStartup<IdempotencyDbContext>(snapshot.MigrationLockTimeout);

        foreach (var purpose in selected)
            services.AddIdempotencyStore<EfCoreIdempotencyStore>(purpose);

        // The metrics singleton holds instruments only; it never touches the scoped IdempotencyDbContext.
        services.AddMetrics();
        services.TryAddSingleton<EfCoreIdempotencyMetrics>();
        services.TryAddSingleton<IRequestContextAccessor, RequestContextAccessor>();

        return services;
    }
}
