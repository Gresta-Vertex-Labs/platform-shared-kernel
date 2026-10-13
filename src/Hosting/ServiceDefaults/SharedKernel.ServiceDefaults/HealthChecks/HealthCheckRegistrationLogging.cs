using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using SharedKernel.ServiceDefaults.Diagnostics;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Logs one structured "health check registered" event (EventId <c>13000</c>) for a readiness check,
/// so every dependency check a service registers appears in its startup log in the same shape.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who calls this.</b> Every <c>Add*ReadinessCheck</c> and <c>Add*HealthCheck</c> extension in the
/// <c>SharedKernel.ServiceDefaults.*</c> integration packages calls it from its registration path. It
/// is public so that those packages — which ship separately from this base — reach it through a
/// stable API rather than <c>InternalsVisibleTo</c>, and so that a service writing its own readiness
/// check can emit the identical event.
/// </para>
/// <para>
/// <b>Why it is public rather than internal.</b> Internals exposed to a separately-published package
/// bind that package to one exact build of this base. NuGet resolves dependency versions as
/// minimums, so a consumer can end up with a newer base than an integration package was compiled
/// against; an internal signature that changed in between then fails at runtime with
/// <see cref="MissingMethodException"/>, not at compile time.
/// </para>
/// <para>
/// <b>When the event is written.</b> No <see cref="IServiceProvider"/> exists while an
/// <c>Add*Check</c> extension runs — those execute against <see cref="IHealthChecksBuilder.Services"/>
/// before the host is built. Rather than building a throwaway provider just to log, this registers a
/// post-configure callback on <see cref="HealthCheckServiceOptions"/>, the options type every
/// <c>Add*Check</c> call ultimately configures. <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>
/// computes and caches that value once for the process lifetime, so the event fires once per
/// registered check when health checks are first resolved — never once per probe invocation.
/// </para>
/// <para>
/// The logger factory is required: resolving the health check options in a host without logging services throws
/// <see cref="InvalidOperationException"/>, so a registration is never silently left unlogged. The category is the
/// package's own marker type, so every registration is traceable to this package, not to a consumer type.
/// </para>
/// <example>
/// How <c>SharedKernel.ServiceDefaults.Persistence</c>'s <c>AddPersistenceStartupReadinessCheck</c> logs its
/// registration; a service writing its own readiness check follows the same shape:
/// <code>
/// public static IHealthChecksBuilder AddPersistenceStartupReadinessCheck(
///     this IHealthChecksBuilder builder,
///     string name = HealthCheckNames.PersistenceStartup)
/// {
///     string[] tags = [HealthCheckTags.Ready, HealthCheckTags.Db];
///     HealthCheckRegistrationLogging.LogRegistration(
///         builder.Services, name, tags);
///     return builder.Add(new HealthCheckRegistration(
///         name,
///         sp => new PersistenceStartupHealthCheck(sp.GetRequiredService&lt;IPersistenceStartup&gt;()),
///         failureStatus: null,
///         tags: tags));
/// }
/// </code>
/// </example>
/// </remarks>
public static class HealthCheckRegistrationLogging
{
    /// <summary>
    /// Arranges for one "health check registered" event to be logged for a single health check
    /// registration, the first time health checks are resolved.
    /// </summary>
    /// <param name="services">The health checks builder's underlying service collection.</param>
    /// <param name="name">The health check's registration name, as passed to <c>AddCheck</c>.</param>
    /// <param name="tags">The health check's registered tags.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="tags"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is <see langword="null"/>, empty, or
    /// whitespace.
    /// </exception>
    public static void LogRegistration(
        IServiceCollection services,
        string name,
        IReadOnlyCollection<string> tags)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(tags);

        var tagsJoined = string.Join(", ", tags);

        services.AddOptions<HealthCheckServiceOptions>()
            .PostConfigure<IServiceProvider>((_, serviceProvider) =>
            {
                var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<ServiceDefaultsLogCategory>();
                ServiceDefaultsLog.HealthCheckRegistered(logger, name, tagsJoined);
            });
    }
}
