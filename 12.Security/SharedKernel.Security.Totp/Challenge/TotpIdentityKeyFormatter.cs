using System.Globalization;

namespace SharedKernel.Security.Totp.Challenge;

/// <summary>
/// The single, shared conversion from a <see cref="Guid"/> user id to the
/// <see cref="string"/>-shaped identity key <see cref="ITotpChallengeStore"/> and
/// <c>01.Core</c>'s <c>TotpVerifier</c>/<c>ITotpReplayGuard</c> key on.
/// </summary>
/// <remarks>
/// Reused by exactly two call sites — <see cref="TotpChallengeService"/> (writer) and
/// <c>StepUp.TotpStepUpClaimsTransformation</c> (reader) — deliberately, so a lookup can never
/// silently miss because one side independently reformatted the same <see cref="Guid"/> differently
/// (WO-069, P-452, D-48).
/// </remarks>
internal static class TotpIdentityKeyFormatter
{
    /// <summary>Formats <paramref name="userId"/> into the canonical identity-key string shape.</summary>
    /// <param name="userId">The user id to format. Must not be <see cref="Guid.Empty"/>.</param>
    /// <returns>The canonical, lowercase, hyphenated <see cref="Guid"/> string form.</returns>
    public static string Format(Guid userId) => userId.ToString("D", CultureInfo.InvariantCulture);
}
