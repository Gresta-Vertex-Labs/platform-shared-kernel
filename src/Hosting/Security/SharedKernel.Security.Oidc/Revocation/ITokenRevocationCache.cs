namespace SharedKernel.Security.Oidc.Revocation;

/// <summary>Caches <see cref="ITokenRevocationCheck"/> answers, keyed by token hash.</summary>
/// <remarks>
/// Implemented by the consuming service. A failure to read or write is logged and the check is called directly, so a
/// broken cache never accepts a revoked token.
/// </remarks>
public interface ITokenRevocationCache
{
    /// <summary>Returns a cached answer.</summary>
    /// <param name="tokenHash">The Base64url SHA-256 hash of the token.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns><see langword="true"/> revoked, <see langword="false"/> not revoked, <see langword="null"/> not cached.</returns>
    ValueTask<bool?> GetAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Caches an answer.</summary>
    /// <param name="tokenHash">The Base64url SHA-256 hash of the token.</param>
    /// <param name="isRevoked">The answer.</param>
    /// <param name="expiresAt">When the entry must expire.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes when the answer is cached.</returns>
    ValueTask SetAsync(string tokenHash, bool isRevoked, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}
