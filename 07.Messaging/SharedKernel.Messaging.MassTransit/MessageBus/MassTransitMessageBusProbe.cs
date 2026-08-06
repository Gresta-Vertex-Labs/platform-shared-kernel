using MassTransit.Monitoring;
using MassTransit.Transports;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Messaging.Abstractions.MessageBus;

namespace SharedKernel.Messaging.MassTransit.MessageBus;

/// <summary>
/// <see cref="IMessageBusProbe"/> implementation querying MassTransit's own bus-health surface —
/// <see cref="BusHealthCheck"/> — against the real, already-registered <see cref="IBusInstance"/>
/// this domain's <c>MessagingBusBuilder</c> builds for the consuming service.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IBusInstance"/> is registered as a singleton by <c>AddMassTransit</c> — this type
/// never constructs an independent, second transport connection. <see cref="BusHealthCheck"/> is
/// MassTransit's own shipped <c>Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck</c>
/// implementation (the same type MassTransit wires into a host's own <c>AddHealthChecks()</c>
/// pipeline via <c>ConfigureBusHealthCheckServiceOptions</c>); this type consumes it directly as an
/// implementation detail rather than re-implementing bus-readiness logic, and never implements
/// <c>IHealthCheck</c> itself — <c>07.Messaging</c> ships no <c>IHealthCheck</c>.
/// </para>
/// <para>
/// Registered as a singleton by <c>MessagingBusBuilder.Build()</c> unconditionally.
/// </para>
/// </remarks>
internal sealed class MassTransitMessageBusProbe : IMessageBusProbe
{
    // Registration name required by BusHealthCheck.CheckHealthAsync (HealthCheckContext.Registration
    // must be non-null — see the "MassTransit 9.x API notes" entry for this discovery). Never surfaced
    // externally; this probe is not itself registered as an ASP.NET Core IHealthCheck.
    private const string RegistrationName = "masstransit-bus";

    private readonly IBusInstance _busInstance;

    public MassTransitMessageBusProbe(IBusInstance busInstance)
    {
        _busInstance = busInstance;
    }

    /// <inheritdoc />
    public async Task<MessageBusHealth> ProbeAsync(CancellationToken ct)
    {
        var check = new BusHealthCheck(_busInstance);
        var registration = new HealthCheckRegistration(RegistrationName, check, failureStatus: null, tags: null);
        var context = new HealthCheckContext { Registration = registration };

        var result = await check.CheckHealthAsync(context, ct).ConfigureAwait(false);

        if (result.Status == HealthStatus.Healthy)
            return new MessageBusHealth(IsHealthy: true, Description: null);

        var description = result.Description
            ?? result.Exception?.Message
            ?? $"Message bus health status is {result.Status}.";

        return new MessageBusHealth(IsHealthy: false, Description: description);
    }
}
