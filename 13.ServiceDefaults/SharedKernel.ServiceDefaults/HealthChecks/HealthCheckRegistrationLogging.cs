using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using SharedKernel.ServiceDefaults.Logging;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Shared helper wiring <see cref="ServiceDefaultsLog.HealthCheckRegistered"/> into every
/// dependency-specific <c>Add*Check</c>/<c>Add*ReadinessCheck</c> extension method's registration
/// path (WO-061/P-395).
/// </summary>
/// <remarks>
/// No <see cref="IServiceProvider"/> exists yet at the point an <c>Add*Check</c> extension method
/// runs — these methods execute against <see cref="IHealthChecksBuilder.Services"/>, before
/// <c>IHostApplicationBuilder.Build()</c>. Rather than eagerly building a throwaway
/// <see cref="IServiceProvider"/> just to log, this helper registers a
/// <see cref="Microsoft.Extensions.Options.IPostConfigureOptions{TOptions}"/> callback on
/// <see cref="HealthCheckServiceOptions"/> — the same options type every <c>Add*Check</c> call
/// ultimately configures. The callback fires exactly once per registered check, the first (and
/// only, since <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> computes and caches
/// its value once for the process lifetime) time <c>HealthCheckServiceOptions</c> is resolved —
/// never once per probe invocation. Depends on <see cref="IServiceProvider"/> (always resolvable,
/// unlike <see cref="ILoggerFactory"/>) and resolves the logger factory optionally — a bare
/// <see cref="IServiceCollection"/> under test with no logging registered (the platform's
/// established baseline for this domain's DI-registration tests) must not fail to resolve
/// <see cref="HealthCheckServiceOptions"/> just because this domain now also logs.
/// </remarks>
internal static class HealthCheckRegistrationLogging
{
    /// <summary>
    /// Wires a one-time <see cref="ServiceDefaultsLog.HealthCheckRegistered"/> log call for a
    /// single dependency-specific health check registration.
    /// </summary>
    /// <param name="services">The health checks builder's underlying service collection.</param>
    /// <param name="categoryName">The logger category name — the fully-qualified extension class name.</param>
    /// <param name="name">The health check's registration name.</param>
    /// <param name="tags">The health check's registered tags.</param>
    public static void LogRegistration(
        IServiceCollection services,
        string categoryName,
        string name,
        IReadOnlyCollection<string> tags)
    {
        var tagsJoined = string.Join(", ", tags);

        services.AddOptions<HealthCheckServiceOptions>()
            .PostConfigure<IServiceProvider>((_, serviceProvider) =>
            {
                var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(categoryName);
                if (logger is not null)
                {
                    ServiceDefaultsLog.HealthCheckRegistered(logger, name, tagsJoined);
                }
            });
    }
}
