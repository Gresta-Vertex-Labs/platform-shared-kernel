using Microsoft.Extensions.Logging;

namespace SharedKernel.MultiTenancy.Diagnostics;

// No tenant id in any message: the resolved tenant reaches every exported log record of the request ambiently, as the
// TenantId record attribute the base's baggage processor copies from the tenant baggage item.
internal static partial class MultiTenancyLog
{
    [LoggerMessage(
        EventId = MultiTenancyLoggingEventIds.TenantResolved,
        Level = LogLevel.Debug,
        Message = "Resolved the tenant with the strategy '{StrategyName}'.")]
    public static partial void TenantResolved(ILogger logger, string strategyName);

    // Trace, not Debug: this fires on every unresolved request including anonymous and health-check traffic. An inactive
    // tenant reuses this message, so it is indistinguishable from an absent one in the log.
    [LoggerMessage(
        EventId = MultiTenancyLoggingEventIds.TenantNotResolved,
        Level = LogLevel.Trace,
        Message = "No tenant resolved for the current request.")]
    public static partial void TenantNotResolved(ILogger logger);
}
