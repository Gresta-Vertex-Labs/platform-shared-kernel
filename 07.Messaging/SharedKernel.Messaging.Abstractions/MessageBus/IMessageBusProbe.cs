namespace SharedKernel.Messaging.Abstractions.MessageBus;

/// <summary>
/// Bus-backed readiness-probe primitive reporting the health of the actual, already-configured
/// message bus the consuming service's <c>MessagingBusBuilder</c> constructs.
/// </summary>
/// <remarks>
/// <para>
/// The implementation must query the real, already-registered bus instance — never an
/// independently constructed connection built from separately supplied configuration. Otherwise a
/// passing probe would not prove the service's actual bus connection is healthy.
/// </para>
/// <para>
/// Registered as a singleton by <c>MessagingBusBuilder.Build()</c> unconditionally, matching
/// MassTransit's own singleton <c>IBus</c>/<c>IBusControl</c> lifetime — no opt-in builder call
/// is required.
/// </para>
/// <para>
/// <c>07.Messaging</c> ships this probe primitive only; it ships <strong>no</strong>
/// <c>IHealthCheck</c> implementation. Wiring <see cref="IMessageBusProbe"/> into
/// <c>AddHealthChecks()</c> remains <c>13.ServiceDefaults</c>'s concern, mirroring the
/// readiness-probe split already established by
/// <c>06.Persistence</c>/<c>08.Storage</c>/<c>09.Search</c>/<c>10.Intelligence</c>/<c>17.Workflows</c>.
/// </para>
/// </remarks>
public interface IMessageBusProbe
{
    /// <summary>
    /// Reports the current health of the message bus.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="MessageBusHealth"/> describing whether the bus is ready.</returns>
    Task<MessageBusHealth> ProbeAsync(CancellationToken ct);
}
