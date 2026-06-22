using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Opt-in Azure Service Bus connectivity health check.
/// </summary>
public static class AzureServiceBusMessagingHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that verifies Azure Service Bus namespace connectivity.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="connectionStringOrNamespace">
    /// Either a full Service Bus connection string (local/dev) or a fully-qualified namespace
    /// hostname (e.g. <c>"my-namespace.servicebus.windows.net"</c>), in which case
    /// <see cref="Azure.Identity.DefaultAzureCredential"/> is used.
    /// </param>
    /// <param name="name">The health check registration name. Defaults to <see cref="HealthCheckNames.AzureServiceBus"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Tagged <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.Messaging"/>.
    /// Opt-in only — never registered by <c>AddServiceDefaults()</c> or
    /// <c>AddSharedKernelHealthChecks()</c>.
    /// </remarks>
    public static IHealthChecksBuilder AddAzureServiceBusMessagingHealthCheck(
        this IHealthChecksBuilder builder,
        string connectionStringOrNamespace,
        string name = HealthCheckNames.AzureServiceBus)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringOrNamespace);

        return builder.Add(new HealthCheckRegistration(
            name,
            _ => new AzureServiceBusHealthCheck(connectionStringOrNamespace),
            failureStatus: null,
            tags: [HealthCheckTags.Ready, HealthCheckTags.Messaging]));
    }
}
