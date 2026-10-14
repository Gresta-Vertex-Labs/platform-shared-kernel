using SharedKernel.Primitives.Logging;

namespace SharedKernel.Security.Totp.Diagnostics;

/// <summary>The EventIds of this package: sub-block 4 of the <see cref="LoggingEventIdRanges.Security"/> block (12400-12499).</summary>
internal static class TotpLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Security + 4 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>A TOTP challenge was not accepted.</summary>
    internal const int ChallengeNotAccepted = Base + 0;

    /// <summary>A TOTP step-up was recorded.</summary>
    internal const int StepUpRecorded = Base + 1;

    /// <summary>A recovery code was redeemed.</summary>
    internal const int RecoveryCodeRedeemed = Base + 2;
}
