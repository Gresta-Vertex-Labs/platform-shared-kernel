using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Seeding;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Opt-in database readiness health checks, wrapping the readiness probe primitives shipped by
/// <c>06.Persistence</c>.
/// </summary>
public static class DatabaseReadinessHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that wraps <see cref="DbContextDiagnosticsExtensions.CheckReadinessAsync"/>
    /// for the registered <typeparamref name="TContext"/>.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="SharedKernelDbContext"/> subclass to probe.</typeparam>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check registration name. Defaults to <see cref="HealthCheckNames.Database"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Tagged <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.Db"/>. Reports
    /// <see cref="HealthStatus.Unhealthy"/> when the probe's <c>IsHealthy</c> is
    /// <see langword="false"/>; <c>Latency</c>/<c>Provider</c> are surfaced via
    /// <see cref="HealthCheckResult.Data"/>. Prefer this overload when a <typeparamref name="TContext"/>
    /// is already registered in DI; use <see cref="AddDapperDatabaseReadinessCheck"/> for
    /// Dapper-only read services that have no <c>DbContext</c>. Opt-in only — never registered by
    /// <c>AddServiceDefaults()</c> or <c>AddSharedKernelHealthChecks()</c>.
    /// </remarks>
    public static IHealthChecksBuilder AddDatabaseReadinessCheck<TContext>(
        this IHealthChecksBuilder builder,
        string name = HealthCheckNames.Database)
        where TContext : SharedKernelDbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        string[] tags = [HealthCheckTags.Ready, HealthCheckTags.Db];

        HealthCheckRegistrationLogging.LogRegistration(
            builder.Services,
            "SharedKernel.ServiceDefaults.HealthChecks.DatabaseReadinessHealthCheckExtensions",
            name,
            tags);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new DatabaseReadinessHealthCheck<TContext>(sp.GetRequiredService<TContext>(), sp.GetService<IPersistenceStartup>()),
            failureStatus: null,
            tags: tags));
    }

    /// <summary>
    /// Registers a health check that wraps <see cref="IDbConnectionFactory"/>'s readiness probe
    /// extension (<c>06.Persistence.Abstractions</c>), for Dapper-only read services that have no
    /// <c>DbContext</c> in scope.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check registration name. Defaults to <see cref="HealthCheckNames.Database"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Tagged <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.Db"/>. Same
    /// <see cref="HealthCheckResult.Data"/> surfacing and tag placement as
    /// <see cref="AddDatabaseReadinessCheck{TContext}"/>. Opt-in only.
    /// </remarks>
    public static IHealthChecksBuilder AddDapperDatabaseReadinessCheck(
        this IHealthChecksBuilder builder,
        string name = HealthCheckNames.DapperDatabase)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string[] tags = [HealthCheckTags.Ready, HealthCheckTags.Db];

        HealthCheckRegistrationLogging.LogRegistration(
            builder.Services,
            "SharedKernel.ServiceDefaults.HealthChecks.DatabaseReadinessHealthCheckExtensions",
            name,
            tags);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new DapperDatabaseReadinessHealthCheck(sp.GetRequiredService<IDbConnectionFactory>()),
            failureStatus: null,
            tags: tags));
    }

    /// <summary>
    /// Registers a readiness check that is Unhealthy until every startup migration and seeder
    /// (<c>MigrateOnStartup()</c>, <c>AddSeeder&lt;T&gt;()</c>) has completed.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The registration name. Defaults to <see cref="HealthCheckNames.PersistenceStartup"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Tagged <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.Db"/>. <see cref="AddDatabaseReadinessCheck{TContext}"/> applies the same gate;
    /// register this one in a service that has no EF Core readiness check. Requires <c>AddSharedKernelPostgres</c>.
    /// </remarks>
    public static IHealthChecksBuilder AddPersistenceStartupReadinessCheck(
        this IHealthChecksBuilder builder,
        string name = HealthCheckNames.PersistenceStartup)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string[] tags = [HealthCheckTags.Ready, HealthCheckTags.Db];
        HealthCheckRegistrationLogging.LogRegistration(builder.Services, typeof(DatabaseReadinessHealthCheckExtensions).FullName!, name, tags);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new PersistenceStartupHealthCheck(sp.GetRequiredService<IPersistenceStartup>()),
            failureStatus: null,
            tags: tags));
    }
}
