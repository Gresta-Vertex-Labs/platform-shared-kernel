namespace SharedKernel.Security.Oidc.Revocation;

/// <summary>
/// Configuration for <see cref="CachingTokenRevocationCheck"/>'s opt-in caching of
/// <see cref="ITokenRevocationCheck"/> outcomes.
/// </summary>
/// <remarks>
/// Only consulted when <c>SecurityAuthenticationBuilder.WithRevocationCheckCaching&lt;TCache&gt;()</c> has
/// been called — revocation-check caching is opt-in and disabled by default, matching the base revocation
/// check's own opt-in posture (WO-060, P-388).
/// </remarks>
public sealed class RevocationCheckCacheOptions
{
    /// <summary>
    /// Gets or sets how long a "not revoked" outcome remains cached.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>30</c> seconds. May be cached more generously than
    /// <see cref="RevokedTtl"/> — a false-negative here only costs one extra introspection call, not a
    /// security gap.
    /// </remarks>
    public TimeSpan NotRevokedTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long a "revoked" outcome remains cached.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>5</c> seconds — DELIBERATELY SHORT. A genuine revocation must never be masked by a
    /// stale "not revoked" cache entry for long, and this TTL bounds how long a "revoked" entry itself is
    /// trusted before the underlying check is consulted again.
    /// </remarks>
    public TimeSpan RevokedTtl { get; set; } = TimeSpan.FromSeconds(5);
}
