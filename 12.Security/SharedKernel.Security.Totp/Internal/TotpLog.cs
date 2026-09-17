using Microsoft.Extensions.Logging;

namespace SharedKernel.Security.Totp.Internal;

// EventIds 12400-12499: SharedKernel.Security.Totp's block within 12.Security's 12000-12999 range.
// Never logs a code, a secret or a recovery code.
internal static partial class TotpLog
{
    [LoggerMessage(
        EventId = 12400,
        Level = LogLevel.Warning,
        Message = "TOTP challenge not accepted (result: {Result}, operation: {Operation}).")]
    public static partial void ChallengeNotAccepted(ILogger logger, TotpChallengeResult result, string operation);

    [LoggerMessage(
        EventId = 12401,
        Level = LogLevel.Information,
        Message = "TOTP step-up completed (operation: {Operation}).")]
    public static partial void StepUpRecorded(ILogger logger, string operation);

    [LoggerMessage(
        EventId = 12402,
        Level = LogLevel.Information,
        Message = "Recovery code redeemed; {Remaining} unused recovery codes remain.")]
    public static partial void RecoveryCodeRedeemed(ILogger logger, int remaining);
}
