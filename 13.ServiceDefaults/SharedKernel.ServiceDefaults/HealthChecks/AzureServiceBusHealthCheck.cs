using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Verifies Azure Service Bus connectivity via <see cref="ServiceBusAdministrationClient"/>,
/// using <see cref="DefaultAzureCredential"/> when a fully-qualified namespace (no connection
/// string) is supplied.
/// </summary>
/// <remarks>
/// <c>AspNetCore.HealthChecks.AzureServiceBus</c> only ships queue/topic/subscription-scoped
/// checks, which require naming a specific entity. This check verifies namespace-level
/// connectivity only, matching the documented
/// <c>AddAzureServiceBusMessagingHealthCheck(connectionStringOrNamespace)</c> contract, which
/// takes no entity name.
/// </remarks>
internal sealed class AzureServiceBusHealthCheck : IHealthCheck
{
    private readonly ServiceBusAdministrationClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusHealthCheck"/> class.
    /// </summary>
    /// <param name="connectionStringOrNamespace">
    /// Either a full Service Bus connection string (detected via
    /// <see cref="AzureServiceBusConnectionStringMarkers"/>) or a fully-qualified namespace
    /// hostname, in which case <see cref="DefaultAzureCredential"/> is used.
    /// </param>
    public AzureServiceBusHealthCheck(string connectionStringOrNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringOrNamespace);

        _client = LooksLikeConnectionString(connectionStringOrNamespace)
            ? new ServiceBusAdministrationClient(connectionStringOrNamespace)
            : new ServiceBusAdministrationClient(connectionStringOrNamespace, new DefaultAzureCredential());
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetNamespacePropertiesAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("Azure Service Bus namespace reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Azure Service Bus namespace unreachable.", ex);
        }
    }

    private static bool LooksLikeConnectionString(string value) =>
        value.Contains(AzureServiceBusConnectionStringMarkers.Endpoint, StringComparison.OrdinalIgnoreCase)
        || value.Contains(AzureServiceBusConnectionStringMarkers.SharedAccessKey, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Substrings that, when present in a connection-string-or-namespace value, indicate the value is
/// a full Service Bus connection string rather than a fully-qualified namespace hostname.
/// </summary>
/// <remarks>
/// Named so the detection intent is documented at the call site instead of relying on bare string
/// literals. A connection string contains both markers; a fully-qualified namespace hostname
/// (e.g. <c>"my-namespace.servicebus.windows.net"</c>) contains neither.
/// </remarks>
internal static class AzureServiceBusConnectionStringMarkers
{
    /// <summary>Marker substring present in the <c>Endpoint=</c> segment of a connection string.</summary>
    public const string Endpoint = "Endpoint=";

    /// <summary>Marker substring present in the <c>SharedAccessKey</c> segment of a connection string.</summary>
    public const string SharedAccessKey = "SharedAccessKey";
}
