using Microsoft.Extensions.Logging;

namespace SharedKernel.ServiceDefaults.Logging;

/// <summary>
/// Structured, source-generated <c>[LoggerMessage]</c> log events for the
/// <c>SharedKernel.ServiceDefaults</c> composition base.
/// </summary>
/// <remarks>
/// <para>
/// <b>EventId allocation.</b> The ServiceDefaults package family — this base plus every
/// <c>SharedKernel.ServiceDefaults.*</c> integration package — shares <c>13000</c>–<c>13099</c>
/// within <c>01.Core</c>'s <c>13000</c>–<c>13999</c> domain block, allocated one EventId at a time
/// and never reused. The sibling <c>SharedKernel.MultiTenancy</c> owns <c>13100</c>–<c>13199</c>.
/// The family shares one sub-block rather than taking one each because it cannot fit: fifteen
/// packages would need fifteen 100-wide sub-blocks, and the domain block holds ten.
/// </para>
/// <para>
/// Current family allocation: <c>13000</c>/<c>13001</c>/<c>13003</c> —
/// <c>SharedKernel.ServiceDefaults.Security.Mtls</c>; <c>13002</c> — this base
/// (<see cref="HealthCheckRegistered"/>); <c>13004</c> —
/// <c>SharedKernel.ServiceDefaults.Localization</c>. These numbers predate the WO-084 package
/// split and were preserved across it, so a dashboard or alert rule filtering on one keeps working.
/// The next free EventId is <c>13005</c>.
/// </para>
/// <para>
/// <b>Never logs a certificate's raw bytes, a raw JWT, or a raw header value</b> — only
/// thumbprint-, subject-, and health-check-name-shaped identifiers and reason strings.
/// CorrelationId/TraceId/TenantId flow ambiently through the OpenTelemetry logging pipeline
/// (<see cref="Telemetry.BaggageLogRecordProcessor"/>), never as an explicit template placeholder.
/// </para>
/// </remarks>
internal static partial class ServiceDefaultsLog
{
    /// <summary>
    /// Logged once per dependency-specific health check registered through
    /// <see cref="HealthChecks.HealthCheckRegistrationLogging.LogRegistration"/> — never once per
    /// probe invocation.
    /// </summary>
    [LoggerMessage(
        EventId = 13002,
        Level = LogLevel.Information,
        Message = "Health check registered (name: {HealthCheckName}, tags: {Tags}).")]
    public static partial void HealthCheckRegistered(ILogger logger, string healthCheckName, string tags);

    /// <summary>
    /// Logged when a readiness probe throws instead of returning an unhealthy report. Only the exception type is
    /// logged, matching what the health endpoint shows.
    /// </summary>
    [LoggerMessage(
        EventId = 13005,
        Level = LogLevel.Warning,
        Message = "Readiness probe {ProbeName} threw {ExceptionType} instead of reporting unhealthy.")]
    public static partial void ReadinessProbeThrew(ILogger logger, string probeName, string exceptionType);
}
