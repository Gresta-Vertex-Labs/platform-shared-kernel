namespace SharedKernel.Security.Oidc.Options;

/// <summary>Settings for the token revocation check.</summary>
public sealed class TokenRevocationOptions
{
    /// <summary>
    /// Gets or sets how long a "not revoked" answer is cached, from zero to five minutes. Defaults to 30 seconds.
    /// </summary>
    /// <remarks>
    /// This is the longest a revoked token can keep working after revocation. A "revoked" answer is cached until the
    /// token expires, because revocation is permanent. Zero disables caching of "not revoked". Used only when a
    /// cache is registered with <c>AddTokenRevocationCache</c>.
    /// </remarks>
    public TimeSpan NotRevokedCacheDuration { get; set; } = TimeSpan.FromSeconds(30);
}
