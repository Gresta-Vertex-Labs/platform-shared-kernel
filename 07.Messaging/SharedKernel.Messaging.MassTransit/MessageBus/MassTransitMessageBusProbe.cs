using System.Diagnostics;
using MassTransit.Monitoring;
using MassTransit.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Primitives.Health;

namespace SharedKernel.Messaging.MassTransit.MessageBus;

/// <summary>
/// The message bus's <see cref="IReadinessProbe"/>, named <see cref="MessagingReadinessProbeNames.Bus"/>. It
/// queries MassTransit's own bus-health surface — <see cref="BusHealthCheck"/> — against the real,
/// already-registered <see cref="IBusInstance"/> this domain's <c>MessagingBusBuilder</c> builds.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IBusInstance"/> is registered as a singleton by <c>AddMassTransit</c> — this type never
/// constructs an independent, second transport connection. <see cref="BusHealthCheck"/> is consumed as an
/// implementation detail rather than re-implementing bus-readiness logic.
/// </para>
/// <para>Registered as a singleton by <c>MessagingBusBuilder.Build()</c> unconditionally.</para>
/// </remarks>
internal sealed class MassTransitMessageBusProbe : IReadinessProbe
{
    // Registration name required by BusHealthCheck.CheckHealthAsync (HealthCheckContext.Registration
    // must be non-null). Never surfaced externally.
    private const string RegistrationName = "masstransit-bus";

    private readonly IServiceProvider _services;

    public MassTransitMessageBusProbe(IServiceProvider services)
    {
        _services = services;
    }

    /// <inheritdoc />
    public string Name => MessagingReadinessProbeNames.Bus;

    /// <inheritdoc />
    public async Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        // Resolved here, not injected: a host constructs every probe just to read its name.
        var check = new BusHealthCheck(_services.GetRequiredService<IBusInstance>());
        var registration = new HealthCheckRegistration(RegistrationName, check, failureStatus: null, tags: null);
        var context = new HealthCheckContext { Registration = registration };

        var result = await check.CheckHealthAsync(context, cancellationToken).ConfigureAwait(false);
        var latency = Stopwatch.GetElapsedTime(started);

        if (result.Status == HealthStatus.Healthy)
            return ReadinessReport.Healthy(latency: latency);

        var description = result.Description
            ?? $"Message bus health status is {result.Status}.";

        return ReadinessReport.Unhealthy(description, latency: latency);
    }
}
