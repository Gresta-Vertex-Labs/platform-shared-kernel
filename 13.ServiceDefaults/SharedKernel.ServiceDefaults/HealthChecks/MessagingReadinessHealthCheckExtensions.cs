using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Messaging.Abstractions.MessageBus;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Opt-in message-bus connectivity health check, wrapping <c>07.Messaging</c>'s
/// <see cref="IMessageBusProbe.ProbeAsync"/> probe.
/// </summary>
public static class MessagingReadinessHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that verifies connectivity of the real, already-configured
    /// message bus (RabbitMQ or Azure Service Bus) via the <see cref="IMessageBusProbe"/> resolved
    /// from DI.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check registration name. Defaults to <see cref="HealthCheckNames.Messaging"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Tagged <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.Messaging"/>, never
    /// <see cref="HealthCheckTags.Live"/>. Resolves only <see cref="IMessageBusProbe"/> from DI —
    /// registered unconditionally as a singleton by <c>07.Messaging.MassTransit</c>'s
    /// <c>MessagingBusBuilder.Build()</c>, with no opt-in call required on that side; the health
    /// check registration itself remains opt-in here, unchanged from every other dependency-specific
    /// check in this domain.
    /// </para>
    /// <para>
    /// Takes <b>no</b> caller-supplied identifier or connection parameter of any kind — mirrors
    /// <see cref="WorkflowReadinessHealthCheckExtensions.AddWorkflowReadinessCheck"/>'s
    /// no-caller-supplied-identifier precedent one step further: <see cref="IMessageBusProbe"/> is a
    /// per-host singleton reflecting whichever single transport the consuming service's own
    /// <c>MessagingBusBuilder</c> already configured, so there is nothing left for a call site to
    /// supply.
    /// </para>
    /// <para>
    /// <b>Replaces the retired <c>AddRabbitMqMessagingHealthCheck</c> and
    /// <c>AddAzureServiceBusMessagingHealthCheck</c> outright (WO-054/P-351)</b> — both independently
    /// constructed a second connection from a caller-supplied connection string instead of
    /// reflecting the real configured bus, a defect no signature-compatible fix could address. A
    /// consuming service must change <c>.AddRabbitMqMessagingHealthCheck(host)</c> /
    /// <c>.AddAzureServiceBusMessagingHealthCheck(connString)</c> to <c>.AddMessagingReadinessCheck()</c>
    /// — dropping the connection argument entirely, since this replacement accepts none.
    /// </para>
    /// <para>
    /// Reports <see cref="HealthStatus.Unhealthy"/> — never <see cref="HealthStatus.Degraded"/> —
    /// when the underlying probe reports the bus as unhealthy. Opt-in only — never registered by
    /// <c>AddServiceDefaults()</c> or <c>AddSharedKernelHealthChecks()</c>.
    /// </para>
    /// </remarks>
    public static IHealthChecksBuilder AddMessagingReadinessCheck(
        this IHealthChecksBuilder builder,
        string name = HealthCheckNames.Messaging)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string[] tags = [HealthCheckTags.Ready, HealthCheckTags.Messaging];

        HealthCheckRegistrationLogging.LogRegistration(
            builder.Services,
            "SharedKernel.ServiceDefaults.HealthChecks.MessagingReadinessHealthCheckExtensions",
            name,
            tags);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new MessagingReadinessHealthCheck(sp.GetRequiredService<IMessageBusProbe>()),
            failureStatus: null,
            tags: tags));
    }
}
