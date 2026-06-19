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
        value.Contains("Endpoint=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("SharedAccessKey", StringComparison.OrdinalIgnoreCase);
}
