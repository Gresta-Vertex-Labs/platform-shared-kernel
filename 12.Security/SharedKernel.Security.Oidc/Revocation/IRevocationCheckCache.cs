namespace SharedKernel.Security.Oidc.Revocation;

/// <summary>
/// A consumer-supplied cache for <see cref="ITokenRevocationCheck"/> outcomes, mitigating the
/// per-request latency/availability cost of a revocation/introspection lookup.
/// </summary>
/// <remarks>
/// <para>
/// The sole consumer-supplied cache extensibility point — mirrors
/// <see cref="SharedKernel.Security.Oidc.Dpop.IDpopProofReplayCache"/>'s "never dictates storage"
/// precedent exactly. This package never references <c>02.Caching</c>; the
/// consumer bridges to their real cache (<c>IMemoryCache</c>, <c>02.Caching.Abstractions</c>, Redis, ...)
/// at their own composition root, mirroring the <c>IUnitOfWork</c>/<c>ITenantContextAccessor</c>
/// local-seam bridge pattern already established in <c>05.Application</c>/<c>07.Messaging</c>
/// (WO-060, P-388).
/// </para>
/// </remarks>
public interface IRevocationCheckCache
{
    /// <summary>
    /// Attempts to read a previously-cached revocation outcome for the given token.
    /// </summary>
    /// <param name="tokenIdentifier">The raw bearer token string, identical to the value passed to
    /// <see cref="ITokenRevocationCheck.IsRevokedAsync"/>.</param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>
    /// The cached revocation outcome (<see langword="true"/> revoked, <see langword="false"/> not
    /// revoked) when a still-valid cache entry exists; <see langword="null"/> on a cache miss.
    /// </returns>
    Task<bool?> TryGetAsync(string tokenIdentifier, CancellationToken ct);

    /// <summary>
    /// Stores a revocation outcome for the given token, valid for the given time-to-live.
    /// </summary>
    /// <param name="tokenIdentifier">The raw bearer token string, identical to the value passed to
    /// <see cref="ITokenRevocationCheck.IsRevokedAsync"/>.</param>
    /// <param name="isRevoked">The outcome to cache.</param>
    /// <param name="ttl">
    /// How long the cached entry remains valid. Callers typically pass
    /// <see cref="RevocationCheckCacheOptions.RevokedTtl"/> or
    /// <see cref="RevocationCheckCacheOptions.NotRevokedTtl"/> depending on <paramref name="isRevoked"/>.
    /// </param>
    /// <param name="ct">A token to observe for cancellation.</param>
    Task SetAsync(string tokenIdentifier, bool isRevoked, TimeSpan ttl, CancellationToken ct);
}
