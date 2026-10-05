using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Health;
using SharedKernel.Scheduling.Hosting;
using SharedKernel.Scheduling.Options;
using SharedKernel.Scheduling.Probes;
using SharedKernel.Scheduling.Registry;

namespace SharedKernel.Scheduling.Extensions;

/// <summary>
/// The composition-root entry point for this package.
/// </summary>
public static class SchedulingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the scheduling hosted loop, the job registration surface, and the readiness-probe
    /// primitive.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">
    /// Optional programmatic overrides for <see cref="SchedulingOptions"/>, applied after
    /// configuration binding (<see cref="SchedulingOptions.SectionName"/>) so explicit code always wins
    /// over a bound config value.
    /// </param>
    /// <returns>
    /// An <see cref="ISchedulingBuilder"/> for registering jobs via <c>AddRecurring</c>/<c>AddDeferred</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Cross-replica single execution requires an <c>IDistributedLockService</c>
    /// (<c>02.Caching.Redis.DistributedLocking</c>'s Redis-backed implementation, or any other
    /// implementation of the same interface) to already be registered in <paramref name="services"/>
    /// before the host starts — this method does not register one itself, and does not require one.
    /// Its absence is a supported, single-replica-only mode that logs a startup <c>Warning</c> naming
    /// the caveat (Domain Invariant 3) rather than failing to start.
    /// </para>
    /// <para>
    /// <see cref="SchedulingOptions"/> is bound from configuration lazily (via
    /// <c>OptionsBuilder&lt;TOptions&gt;.BindConfiguration</c>, which resolves <c>IConfiguration</c>
    /// from the container at options-creation time) rather than through
    /// <c>SharedKernel.Configuration</c>'s <c>AddValidatedOptions&lt;TOptions&gt;(IConfigurationSection)</c>
    /// helper, since that helper requires an already-resolved <c>IConfigurationSection</c> and this
    /// method's signature — <c>Action&lt;SchedulingOptions&gt;? configure = null</c>, matching the
    /// platform's other <c>AddSharedKernelX</c> entry points — takes no <c>IConfiguration</c> parameter.
    /// Both paths produce the same externally observable contract: bound from
    /// <see cref="SchedulingOptions.SectionName"/>, validated eagerly at <c>IHost.StartAsync()</c>, and
    /// SK0022-compliant (the section path is never a bare literal at a call site).
    /// </para>
    /// </remarks>
    public static ISchedulingBuilder AddSharedKernelScheduling(
        this IServiceCollection services,
        Action<SchedulingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        OptionsBuilder<SchedulingOptions> optionsBuilder = services
            .AddOptions<SchedulingOptions>()
            .BindConfiguration(SchedulingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configure is not null)
        {
            optionsBuilder.PostConfigure(configure);
        }

        var registry = new ScheduledJobRegistry(services);
        services.TryAddSingleton(registry);
        services.TryAddSingleton<IScheduledJobRegistry>(sp => sp.GetRequiredService<ScheduledJobRegistry>());

        services.TryAddSingleton<SchedulingHostedService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<SchedulingHostedService>());
        services.AddReadinessProbe<SchedulerServiceProbe>();

        return registry;
    }
}
