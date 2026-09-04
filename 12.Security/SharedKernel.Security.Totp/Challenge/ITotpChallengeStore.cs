namespace SharedKernel.Security.Totp.Challenge;

/// <summary>
/// Tracks the most recent successful TOTP challenge per identity, so the step-up claims
/// transformation can decide whether "was TOTP verified recently" still holds.
/// </summary>
/// <remarks>
/// <para>
/// The sole consumer-supplied extensibility point — mirrors
/// <c>SharedKernel.Security.Oidc</c>'s <c>IDpopProofReplayCache</c>/<c>ITokenRevocationCheck</c>
/// "never dictates storage" precedent exactly. A typical implementation is in-memory for a
/// single-instance development host, or backed by a distributed cache/database for a
/// production, multi-replica deployment. This package never references <c>02.Caching</c> or
/// <c>06.Persistence</c> (WO-069, P-452).
/// </para>
/// <para>
/// <paramref name="identityKey">identityKey</paramref>-shaped parameters use the SAME
/// <see cref="string"/> shape <c>01.Core</c>'s <c>TotpVerifier</c>/<c>ITotpReplayGuard</c> already key
/// on — never a divergent <see cref="Guid"/>-keyed contract. A <see cref="Guid"/> user id is always
/// formatted to this shape via the single internal helper this package reuses for both the writer
/// (<see cref="TotpChallengeService"/>) and the reader (<c>TotpStepUpClaimsTransformation</c>).
/// </para>
/// </remarks>
public interface ITotpChallengeStore
{
    /// <summary>
    /// Records that <paramref name="identityKey"/> just completed a successful TOTP (or
    /// recovery-code) challenge at <paramref name="verifiedAt"/>.
    /// </summary>
    /// <param name="identityKey">The formatted identity key the challenge was verified for.</param>
    /// <param name="verifiedAt">The UTC instant the challenge succeeded.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RecordSuccessfulChallengeAsync(string identityKey, DateTimeOffset verifiedAt, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the instant of the most recent successful TOTP (or recovery-code) challenge
    /// recorded for <paramref name="identityKey"/>, if any.
    /// </summary>
    /// <param name="identityKey">The formatted identity key to look up.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// The UTC instant of the most recent successful challenge, or <see langword="null"/> when none
    /// has ever been recorded for this identity.
    /// </returns>
    Task<DateTimeOffset?> TryGetLastSuccessfulChallengeAsync(string identityKey, CancellationToken ct = default);
}
