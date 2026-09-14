using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Messaging.Abstractions.MessageBus;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Wraps <see cref="IMessageBusProbe.ProbeAsync"/> (<c>07.Messaging</c>) in an
/// <see cref="IHealthCheck"/>.
/// </summary>
/// <remarks>
/// <para>
/// Reports <see cref="HealthStatus.Healthy"/> when <see cref="MessageBusHealth.IsHealthy"/> is
/// <see langword="true"/>; <see cref="HealthStatus.Unhealthy"/> otherwise — never
/// <see cref="HealthStatus.Degraded"/>, because no fail-safe/graceful-degradation layer sits in
/// front of raw message-bus connectivity (unlike <c>CacheReadinessHealthCheck</c>'s
/// FusionCache-L1-absorption rationale). <see cref="MessageBusHealth.Description"/> is surfaced
/// via <see cref="HealthCheckResult.Description"/>.
/// </para>
/// <para>
/// <c>07.Messaging</c> ships only the <see cref="IMessageBusProbe"/> probe primitive — this
/// adapter is the <c>13.ServiceDefaults</c>-owned <see cref="IHealthCheck"/> wiring per the
/// platform's "OTel/health check/probe wiring lives in 13.ServiceDefaults" rule. The probe itself
/// is registered unconditionally as a singleton by <c>MessagingBusBuilder.Build()</c> and reflects
/// the real, already-configured bus for whichever single transport (RabbitMQ or Azure Service Bus)
/// the consuming service configured — never an independently constructed connection.
/// </para>
/// <para>
/// This type replaces the retired <c>AzureServiceBusHealthCheck</c> (WO-054/P-351), which
/// independently constructed a second, unrelated connection from a caller-supplied connection
/// string instead of reflecting the real configured bus.
/// </para>
/// </remarks>
internal sealed class MessagingReadinessHealthCheck(IMessageBusProbe probe) : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var health = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);

        return health.IsHealthy
            ? HealthCheckResult.Healthy(health.Description)
            : HealthCheckResult.Unhealthy(health.Description);
    }
}
