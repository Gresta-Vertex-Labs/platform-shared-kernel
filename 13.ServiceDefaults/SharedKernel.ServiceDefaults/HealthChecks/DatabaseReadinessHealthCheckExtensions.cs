using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;

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

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new DatabaseReadinessHealthCheck<TContext>(sp.GetRequiredService<TContext>()),
            failureStatus: null,
            tags: [HealthCheckTags.Ready, HealthCheckTags.Db]));
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
        string name = HealthCheckNames.Database)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new DapperDatabaseReadinessHealthCheck(sp.GetRequiredService<IDbConnectionFactory>()),
            failureStatus: null,
            tags: [HealthCheckTags.Ready, HealthCheckTags.Db]));
    }
}
