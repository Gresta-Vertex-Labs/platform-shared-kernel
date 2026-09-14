using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using SharedKernel.ServiceDefaults.Logging;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Logs one structured "health check registered" event (EventId <c>13002</c>) for a readiness check,
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
/// The logger factory is resolved optionally: a bare <see cref="IServiceCollection"/> with no logging
/// registered — the usual shape of a DI registration test — still resolves
/// <see cref="HealthCheckServiceOptions"/> and simply writes nothing.
/// </para>
/// <example>
/// A readiness check written outside this platform, logging like the built-in ones:
/// <code>
/// public static IHealthChecksBuilder AddLedgerReadinessCheck(this IHealthChecksBuilder builder)
/// {
///     string[] tags = [HealthCheckTags.Ready];
///     HealthCheckRegistrationLogging.LogRegistration(
///         builder.Services, typeof(LedgerHealthCheckExtensions).FullName!, "ledger", tags);
///     return builder.AddCheck&lt;LedgerReadinessHealthCheck&gt;("ledger", tags: tags);
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
    /// <param name="categoryName">
    /// The logger category — by convention the fully-qualified name of the extension class doing the
    /// registering, so the event can be traced to the package that registered the check.
    /// </param>
    /// <param name="name">The health check's registration name, as passed to <c>AddCheck</c>.</param>
    /// <param name="tags">The health check's registered tags.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="tags"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="categoryName"/> or <paramref name="name"/> is <see langword="null"/>, empty, or
    /// whitespace.
    /// </exception>
    public static void LogRegistration(
        IServiceCollection services,
        string categoryName,
        string name,
        IReadOnlyCollection<string> tags)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(tags);

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
