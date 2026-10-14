using Microsoft.Extensions.Logging;

namespace SharedKernel.ServiceDefaults.Localization.Diagnostics;

internal static partial class ServiceDefaultsLocalizationLog
{
    [LoggerMessage(
        EventId = ServiceDefaultsLocalizationLoggingEventIds.NoDynamicCultureStepCanResolve,
        Level = LogLevel.Warning,
        Message = "Neither a user preference claim type nor a tenant catalog is configured, so the user preference and tenant default culture steps can never resolve a culture.")]
    public static partial void NoDynamicCultureStepCanResolve(ILogger logger);
}
