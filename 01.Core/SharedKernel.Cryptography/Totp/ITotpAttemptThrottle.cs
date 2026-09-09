namespace SharedKernel.Cryptography.Totp;

/// <summary>
/// A standalone attempt-throttling seam for TOTP verification, satisfying RFC 4226 §7.3's
/// recommendation to rate-limit verification attempts. Implemented by the consuming service —
/// this package ships no default implementation.
/// </summary>
/// <remarks>
/// <para>
/// A 6-digit code with a ±1-step drift window (the <see cref="TotpVerifier"/> defaults) has a
/// small enough keyspace to be brute-forceable absent a rate limit on verification attempts. This
/// interface exists to let a consuming service enforce one, but it is deliberately NOT wired into
/// <see cref="TotpVerifier"/>'s constructor and <see cref="TotpVerifier.VerifyAsync"/> never calls
/// it — attempt throttling is the caller's own concern (it decides lockout duration, HTTP 429
/// shaping, audit logging, etc.), mirroring how <see cref="ITotpReplayGuard"/> itself, and
/// <c>12.Security.Oidc</c>'s DPoP replay-check/<c>ITokenRevocationCheck</c> seams, all ship
/// uninvolved and let the consumer compose them.
/// </para>
/// <para>
/// A typical caller checks <see cref="IsThrottledAsync"/> before invoking
/// <see cref="TotpVerifier.VerifyAsync"/>, and calls <see cref="RecordAttemptAsync"/> for every
/// attempt (successful or not) so a burst of guesses trips the throttle regardless of outcome.
/// </para>
/// </remarks>
public interface ITotpAttemptThrottle
{
    /// <summary>
    /// Checks whether verification attempts for <paramref name="identityKey"/> are currently
    /// throttled (i.e. the caller should reject the attempt without even reaching
    /// <see cref="TotpVerifier.VerifyAsync"/>).
    /// </summary>
    /// <param name="identityKey">The identity being challenged (e.g. a user id or enrollment id).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><see langword="true"/> when further attempts for this identity should currently be rejected.</returns>
    ValueTask<bool> IsThrottledAsync(string identityKey, CancellationToken ct = default);

    /// <summary>
    /// Records that a verification attempt was made for <paramref name="identityKey"/>, so a
    /// burst of attempts can trip <see cref="IsThrottledAsync"/> regardless of whether any
    /// individual attempt succeeded.
    /// </summary>
    /// <param name="identityKey">The identity being challenged.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask RecordAttemptAsync(string identityKey, CancellationToken ct = default);
}
