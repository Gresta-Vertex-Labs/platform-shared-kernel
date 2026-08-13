using Microsoft.Extensions.Logging;

namespace SharedKernel.Security.ApiKey.Logging;

/// <summary>
/// Structured, source-generated <c>[LoggerMessage]</c> security-audit log events for
/// <c>SharedKernel.Security.ApiKey</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reserves <c>EventId</c> sub-range <c>12200</c>–<c>12299</c> within <c>01.Core</c>'s
/// <c>LoggingEventIdRanges.Security</c> (<c>12000</c>–<c>12999</c>) block, per the platform's WO-041
/// logging convention (WO-057, P-371).
/// </para>
/// <para>
/// <b>Never logs a raw claim value, raw token content, or a raw API key</b> — only structured, safe
/// fields (a failure-reason string).
/// </para>
/// </remarks>
internal static partial class SecurityLogEvents
{
    /// <summary>
    /// Logged when API-key authentication fails — the presented key was rejected by
    /// <c>IApiKeyValidator</c>, or the header and query-string credentials on the same request disagreed.
    /// </summary>
    [LoggerMessage(
        EventId = 12200,
        Level = LogLevel.Warning,
        Message = "API key authentication failed (reason: {FailureReason}).")]
    public static partial void ApiKeyValidationFailed(ILogger logger, string failureReason);
}
