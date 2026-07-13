using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Communication.Internal.Extensions;

/// <summary>
/// Emits a <see cref="LogLevel.Warning"/> log at startup when <c>StaticServiceEndpointResolver</c>
/// is registered. This is a deliberate reminder that static discovery is not production-safe.
/// </summary>
internal sealed partial class StaticServiceDiscoveryStartupWarning(
    ILogger<StaticServiceDiscoveryStartupWarning> logger,
    IReadOnlyDictionary<string, Uri> endpoints) : IHostedService
{
    [LoggerMessage(
        EventId = 11308,
        Level = LogLevel.Warning,
        Message = "StaticServiceEndpointResolver is active with {EndpointCount} mapped endpoint(s). "
            + "This resolver is for non-production environments only. "
            + "Replace with AddK8sServiceDiscovery() in production.")]
    private static partial void LogStaticServiceDiscoveryActive(ILogger logger, int endpointCount);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogStaticServiceDiscoveryActive(logger, endpoints.Count);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
