using Microsoft.Extensions.Logging;

namespace SharedKernel.ServiceDefaults.Security.Diagnostics;

// Never logs a rejected correlation id: an unvalidated caller-controlled string is a log-injection vector, so only its
// length is logged. The accepted or created id reaches every record ambiently through the baggage log processor.
internal static partial class ServiceDefaultsSecurityLog
{
    [LoggerMessage(
        EventId = ServiceDefaultsSecurityLoggingEventIds.CorrelationIdCreated,
        Level = LogLevel.Debug,
        Message = "Created a correlation id for a request that carried none.")]
    public static partial void CorrelationIdCreated(ILogger logger);

    [LoggerMessage(
        EventId = ServiceDefaultsSecurityLoggingEventIds.CorrelationIdRejected,
        Level = LogLevel.Warning,
        Message = "Replaced an inbound correlation id of length {CorrelationIdLength} that failed validation.")]
    public static partial void CorrelationIdRejected(ILogger logger, int correlationIdLength);
}
