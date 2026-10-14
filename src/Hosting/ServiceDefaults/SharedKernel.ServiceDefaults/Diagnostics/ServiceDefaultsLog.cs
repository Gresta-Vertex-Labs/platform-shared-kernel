using Microsoft.Extensions.Logging;

namespace SharedKernel.ServiceDefaults.Diagnostics;

// Never logs a tenant id, a correlation id, a remote address or an exception message. Those flow ambiently through the
// OpenTelemetry logging pipeline (BaggageLogRecordProcessor) or stay out of the log.
internal static partial class ServiceDefaultsLog
{
    [LoggerMessage(
        EventId = ServiceDefaultsLoggingEventIds.HealthCheckRegistered,
        Level = LogLevel.Information,
        Message = "Registered the health check '{HealthCheckName}' with the tags '{Tags}'.")]
    public static partial void HealthCheckRegistered(ILogger logger, string healthCheckName, string tags);

    [LoggerMessage(
        EventId = ServiceDefaultsLoggingEventIds.ReadinessProbeThrew,
        Level = LogLevel.Error,
        Message = "The readiness probe '{ProbeName}' threw an exception instead of reporting its state.")]
    public static partial void ReadinessProbeThrew(ILogger logger, string probeName, Exception exception);
}
