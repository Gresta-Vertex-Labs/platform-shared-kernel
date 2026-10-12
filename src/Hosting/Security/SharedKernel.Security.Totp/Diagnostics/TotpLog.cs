using Microsoft.Extensions.Logging;

namespace SharedKernel.Security.Totp.Diagnostics;

// Never logs a code, a secret or a recovery code.
internal static partial class TotpLog
{
    [LoggerMessage(
        EventId = TotpLoggingEventIds.ChallengeNotAccepted,
        Level = LogLevel.Warning,
        Message = "The TOTP challenge was not accepted with result '{Result}' during operation '{Operation}'.")]
    public static partial void ChallengeNotAccepted(ILogger logger, TotpChallengeResult result, string operation);

    [LoggerMessage(
        EventId = TotpLoggingEventIds.StepUpRecorded,
        Level = LogLevel.Information,
        Message = "The TOTP step-up completed during operation '{Operation}'.")]
    public static partial void StepUpRecorded(ILogger logger, string operation);

    [LoggerMessage(
        EventId = TotpLoggingEventIds.RecoveryCodeRedeemed,
        Level = LogLevel.Information,
        Message = "A recovery code was redeemed and {Remaining} unused recovery codes remain.")]
    public static partial void RecoveryCodeRedeemed(ILogger logger, int remaining);
}
