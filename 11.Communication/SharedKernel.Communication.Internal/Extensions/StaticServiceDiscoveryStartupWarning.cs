using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Communication.Internal.Extensions;

/// <summary>
/// Emits a <see cref="LogLevel.Warning"/> log at startup when <c>StaticServiceEndpointResolver</c>
/// is registered. This is a deliberate reminder that static discovery is not production-safe.
/// </summary>
internal sealed class StaticServiceDiscoveryStartupWarning(
    ILogger<StaticServiceDiscoveryStartupWarning> logger,
    IReadOnlyDictionary<string, Uri> endpoints) : IHostedService
{
    private static readonly Action<ILogger, int, Exception?> _logWarning =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(100, "StaticServiceDiscoveryActive"),
            "StaticServiceEndpointResolver is active with {EndpointCount} mapped endpoint(s). " +
            "This resolver is for non-production environments only. " +
            "Replace with AddK8sServiceDiscovery() in production.");

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logWarning(logger, endpoints.Count, null);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
