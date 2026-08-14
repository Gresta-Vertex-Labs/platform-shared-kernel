namespace SharedKernel.Security.Oidc.Dpop;

/// <summary>
/// A consumer-supplied replay-detection store for DPoP (RFC 9449) proof JWTs.
/// </summary>
/// <remarks>
/// <para>
/// The sole consumer-supplied extensibility point for DPoP validation, opted into via
/// <c>SecurityAuthenticationBuilder.RequireDpop&lt;TReplayCache&gt;()</c>. Mirrors
/// <c>SharedKernel.Security.ApiKey.Validation.IApiKeyValidator</c>'s extensibility pattern exactly.
/// </para>
/// <para>
/// This package never references <c>02.Caching</c> or dictates a storage mechanism — the consuming
/// service is free to back this with a distributed cache, a database table, or an in-memory store keyed
/// on the proof's <c>jti</c> claim (WO-058, P-376).
/// </para>
/// </remarks>
public interface IDpopProofReplayCache
{
    /// <summary>
    /// Attempts to record a DPoP proof's unique token identifier as consumed, detecting replay.
    /// </summary>
    /// <param name="jti">The DPoP proof JWT's unique <c>jti</c> claim value.</param>
    /// <param name="proofExpiresAt">
    /// The instant after which <paramref name="jti"/> no longer needs to be retained by the
    /// implementation (typically the proof's freshness-window expiry).
    /// </param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="jti"/> had not been seen before (first use — the
    /// request may proceed); <see langword="false"/> when <paramref name="jti"/> was already recorded (a
    /// replay — the request must be rejected).
    /// </returns>
    Task<bool> TryConsumeAsync(string jti, DateTimeOffset proofExpiresAt, CancellationToken ct);
}
