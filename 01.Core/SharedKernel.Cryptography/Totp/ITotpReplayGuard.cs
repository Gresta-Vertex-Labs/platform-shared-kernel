namespace SharedKernel.Cryptography.Totp;

/// <summary>
/// Tracks previously-accepted TOTP codes so the same code cannot be accepted twice inside its
/// validity window. Implemented by the consuming service — this package ships no default
/// implementation.
/// </summary>
/// <remarks>
/// Mirrors <c>12.Security.Oidc</c>'s DPoP replay-check seam: a pluggable contract, never a direct
/// <c>02.Caching</c> reference from this package. A typical implementation is backed by a
/// distributed cache/store keyed by <c>identityKey</c> (e.g. the user id or TOTP enrollment id)
/// with an entry expiring after <c>validityWindow</c> so the store never grows unbounded.
/// </remarks>
public interface ITotpReplayGuard
{
    /// <summary>
    /// Checks whether <paramref name="code"/> has already been accepted for
    /// <paramref name="identityKey"/> and is still inside its recorded validity window.
    /// </summary>
    /// <param name="identityKey">The identity the code was presented for (e.g. a user id or enrollment id).</param>
    /// <param name="code">The candidate code.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><see langword="true"/> when this exact code has already been accepted and marked used for this identity.</returns>
    ValueTask<bool> HasBeenUsedAsync(string identityKey, string code, CancellationToken ct = default);

    /// <summary>
    /// Records that <paramref name="code"/> has just been accepted for
    /// <paramref name="identityKey"/>, valid to be rejected as a replay for
    /// <paramref name="validityWindow"/>.
    /// </summary>
    /// <param name="identityKey">The identity the code was presented for.</param>
    /// <param name="code">The code that was just accepted.</param>
    /// <param name="validityWindow">How long this code should continue to be reported as used by <see cref="HasBeenUsedAsync"/>.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask MarkUsedAsync(string identityKey, string code, TimeSpan validityWindow, CancellationToken ct = default);
}
