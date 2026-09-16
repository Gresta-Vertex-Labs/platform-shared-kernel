namespace SharedKernel.Cryptography.Totp;

/// <summary>Limits how many one-time codes can be tried per identity, as RFC 4226 section 7.3 requires.</summary>
/// <remarks>
/// Implemented by the consuming service, which decides the limit, the lockout and how a rejection is reported.
/// <see cref="ITotpVerifier"/> does not call it. Check <see cref="IsThrottledAsync"/> before verifying and call
/// <see cref="RecordAttemptAsync"/> for every attempt, successful or not.
/// </remarks>
public interface ITotpAttemptThrottle
{
    /// <summary>Returns whether attempts for <paramref name="identityKey"/> are currently blocked.</summary>
    /// <param name="identityKey">The enrollment being challenged.</param>
    /// <param name="cancellationToken">A token to cancel the check.</param>
    /// <returns><see langword="true"/> when the attempt should be rejected without verifying.</returns>
    ValueTask<bool> IsThrottledAsync(string identityKey, CancellationToken cancellationToken = default);

    /// <summary>Records an attempt for <paramref name="identityKey"/>.</summary>
    /// <param name="identityKey">The enrollment being challenged.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the attempt is recorded.</returns>
    ValueTask RecordAttemptAsync(string identityKey, CancellationToken cancellationToken = default);
}
