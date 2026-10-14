using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Health;
using SharedKernel.ServiceDefaults.Diagnostics;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>Runs one <see cref="IReadinessProbe"/> as a health check.</summary>
internal sealed class ReadinessProbeHealthCheck(IReadinessProbe probe, ILogger<ReadinessProbeHealthCheck> logger) : IHealthCheck
{
    private readonly ILogger<ReadinessProbeHealthCheck> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>The data key the report's latency is written under.</summary>
    internal const string LatencyDataKey = "LatencyMilliseconds";

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var failureStatus = context.Registration?.FailureStatus ?? HealthStatus.Unhealthy;
        var started = Stopwatch.GetTimestamp();

        ReadinessReport report;
        try
        {
            report = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ServiceDefaultsLog.ReadinessProbeThrew(_logger, probe.Name, exception);

            return new HealthCheckResult(
                failureStatus,
                $"The readiness probe failed ({exception.GetType().Name}).",
                data: new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [LatencyDataKey] = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                });
        }

        var data = new Dictionary<string, object>(report.Data, StringComparer.Ordinal);
        if (report.Latency is { } latency)
            data[LatencyDataKey] = latency.TotalMilliseconds;

        var status = report.Status switch
        {
            ReadinessStatus.Healthy => HealthStatus.Healthy,
            ReadinessStatus.Degraded => HealthStatus.Degraded,
            _ => failureStatus,
        };

        return new HealthCheckResult(status, report.Description, data: data);
    }
}
