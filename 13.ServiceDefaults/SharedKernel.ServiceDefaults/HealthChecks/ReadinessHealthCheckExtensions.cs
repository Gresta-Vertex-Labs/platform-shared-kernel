using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Health;
using SharedKernel.ServiceDefaults.Logging;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Maps every registered <see cref="IReadinessProbe"/> to a health check on <c>/health/ready</c>.
/// </summary>
/// <remarks>
/// <para>
/// Provider packages register their own probes when the provider is registered — <c>AddRedisConnection</c> the Redis
/// probe, <c>MessagingBusBuilder.Build()</c> the bus probe, each named storage store and search index its own probe,
/// and so on. This base references none of them: it only knows <see cref="IReadinessProbe"/>, which lives in the
/// Foundation-tier <c>SharedKernel.Primitives</c>. One call therefore covers every dependency the service actually
/// uses, and a dependency the service does not use has no probe to map.
/// </para>
/// <para>
/// Probes are read when health checks are first resolved, so the order of this call and the provider registrations
/// does not matter.
/// </para>
/// </remarks>
public static class ReadinessHealthCheckExtensions
{
    /// <summary>
    /// Registers one health check per registered <see cref="IReadinessProbe"/>, named after the probe and tagged
    /// <see cref="HealthCheckTags.Ready"/>. Calling it more than once is harmless; the options of the last call win.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="configure">Excludes probes or sets a per-check timeout; optional.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="ReadinessStatus.Degraded"/> maps to <see cref="HealthStatus.Degraded"/> and
    /// <see cref="ReadinessStatus.Unhealthy"/> to <see cref="HealthStatus.Unhealthy"/>. A probe that throws (it should
    /// not) is reported as unhealthy with the exception type only. The report's latency, when present, is added to
    /// the check's data as <c>LatencyMilliseconds</c>.
    /// </para>
    /// <para>
    /// Two probes with the same name, or a probe named like a check registered some other way, make health-check
    /// resolution fail with an exception naming the duplicate.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.AddServiceDefaults();
    /// builder.Services.AddHealthChecks()
    ///     .AddDatabaseReadinessCheck&lt;OrdersDbContext&gt;()
    ///     .AddSharedKernelReadiness(o => o.Exclude(CacheReadinessProbeNames.Cache));
    /// </code>
    /// </example>
    public static IHealthChecksBuilder AddSharedKernelReadiness(
        this IHealthChecksBuilder builder,
        Action<ReadinessHealthCheckOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = builder.Services.AddOptions<ReadinessHealthCheckOptions>();
        if (configure is not null)
            options.Configure(configure);

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<HealthCheckServiceOptions>, ReadinessProbeRegistrar>());

        return builder;
    }

    /// <summary>Adds one registration per probe to <see cref="HealthCheckServiceOptions"/>.</summary>
    private sealed class ReadinessProbeRegistrar(
        IServiceProvider services,
        IOptions<ReadinessHealthCheckOptions> readinessOptions) : IConfigureOptions<HealthCheckServiceOptions>
    {
        private const string LoggerCategory = "SharedKernel.ServiceDefaults.HealthChecks.ReadinessHealthCheckExtensions";

        public void Configure(HealthCheckServiceOptions options)
        {
            var settings = readinessOptions.Value;
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory);
            var existing = options.Registrations.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var probe in services.GetServices<IReadinessProbe>())
            {
                var name = probe.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException(
                        $"The readiness probe {probe.GetType().FullName} has no name.");
                }

                if (settings.ExcludedProbes.Contains(name))
                    continue;

                if (!existing.Add(name))
                {
                    throw new InvalidOperationException(
                        $"More than one health check is named '{name}'. Readiness probe names must be unique; exclude one with AddSharedKernelReadiness(o => o.Exclude(\"{name}\")) or rename the other check.");
                }

                string[] tags = [HealthCheckTags.Ready];
                options.Registrations.Add(new HealthCheckRegistration(
                    name,
                    sp => new ReadinessProbeHealthCheck(probe, sp.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory)),
                    failureStatus: null,
                    tags: tags,
                    timeout: settings.Timeout));

                if (logger is not null)
                    ServiceDefaultsLog.HealthCheckRegistered(logger, name, string.Join(", ", tags));
            }
        }
    }
}
