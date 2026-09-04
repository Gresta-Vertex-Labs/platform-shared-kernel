using Microsoft.Extensions.Logging;

namespace SharedKernel.Security.Totp.Logging;

/// <summary>
/// Structured, source-generated <c>[LoggerMessage]</c> security-audit log events for
/// <c>SharedKernel.Security.Totp</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reserves <c>EventId</c> sub-range <c>12400</c>–<c>12499</c> within <c>01.Core</c>'s
/// <c>LoggingEventIdRanges.Security</c> (<c>12000</c>–<c>12999</c>) block — the fifth per-package
/// sub-block within the domain's range, per the platform's WO-041 logging convention (WO-069, P-452).
/// </para>
/// <para>
/// <b>Never logs a raw TOTP code, raw secret, or raw recovery code</b> — only structured, safe
/// fields (a failure-reason string, or nothing at all).
/// </para>
/// </remarks>
internal static partial class SecurityLogEvents
{
    /// <summary>
    /// Logged when <see cref="Challenge.TotpChallengeService.VerifyAsync"/> rejects a presented code.
    /// </summary>
    [LoggerMessage(
        EventId = 12400,
        Level = LogLevel.Warning,
        Message = "TOTP challenge rejected (reason: {FailureReason}).")]
    public static partial void TotpChallengeRejected(ILogger logger, string failureReason);

    /// <summary>
    /// Logged when <see cref="StepUp.TotpStepUpClaimsTransformation"/> stamps the configured AMR claim
    /// onto the current principal after finding a fresh successful challenge. Routine/informational —
    /// not an error.
    /// </summary>
    [LoggerMessage(
        EventId = 12401,
        Level = LogLevel.Debug,
        Message = "TOTP step-up claim applied to the current principal.")]
    public static partial void TotpStepUpClaimApplied(ILogger logger);
}
