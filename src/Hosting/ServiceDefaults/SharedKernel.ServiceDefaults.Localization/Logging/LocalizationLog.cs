using Microsoft.Extensions.Logging;

namespace SharedKernel.ServiceDefaults.Logging;

/// <summary>
/// Structured, source-generated <c>[LoggerMessage]</c> log events for request-culture resolution.
/// </summary>
/// <remarks>
/// EventId <c>13004</c> belongs to the ServiceDefaults package family's shared
/// <c>13000</c>–<c>13099</c> sub-block. It moved here unchanged from the composition base when
/// WO-084 split it, so existing dashboards and alert rules keep matching. See
/// <c>SharedKernel.ServiceDefaults.Logging.ServiceDefaultsLog</c> for the full family allocation.
/// </remarks>
internal static partial class LocalizationLog
{
    /// <summary>
    /// Logged once at startup by
    /// <see cref="Localization.LocalizationExtensions.AddSharedKernelLocalization"/> when neither the
    /// <c>UserPreference</c> nor the <c>TenantDefault</c> culture-resolution step can ever resolve a
    /// culture — <c>LocalizationResolutionOptions.UserPreferenceClaimType</c> is unconfigured and no
    /// <c>ITenantCatalog</c> is registered.
    /// </summary>
    [LoggerMessage(
        EventId = 13004,
        Level = LogLevel.Warning,
        Message = "AddSharedKernelLocalization: neither UserPreferenceClaimType nor a registered ITenantCatalog is configured — the UserPreference and TenantDefault culture-resolution steps can never resolve a culture.")]
    public static partial void LocalizationNoDynamicStrategyCanResolve(ILogger logger);
}
