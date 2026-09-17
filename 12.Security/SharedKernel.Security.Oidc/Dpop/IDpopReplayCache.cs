namespace SharedKernel.Security.Oidc.Dpop;

/// <summary>Remembers DPoP proofs already used, so a captured proof cannot be sent again (RFC 9449 section 11.1).</summary>
/// <remarks>
/// Implemented by the consuming service over storage shared by every replica, such as Redis <c>SET NX PX</c> or a
/// table with a unique key. The check and the insert must be one atomic operation.
/// </remarks>
public interface IDpopReplayCache
{
    /// <summary>Records a proof as used, unless it was already recorded.</summary>
    /// <param name="proofId">
    /// A fixed-length identifier derived from the client's key and the proof's <c>jti</c>; safe to use as a storage
    /// key.
    /// </param>
    /// <param name="expiresAt">When the entry may be deleted: the proof can no longer be accepted after this time.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> when the proof was recorded now; <see langword="false"/> when it was already recorded,
    /// which rejects the request. Throw when the store is unavailable; the request is then rejected.
    /// </returns>
    ValueTask<bool> TryAddAsync(string proofId, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}
