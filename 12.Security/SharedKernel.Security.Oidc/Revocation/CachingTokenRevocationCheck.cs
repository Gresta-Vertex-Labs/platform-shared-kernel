using Microsoft.Extensions.Options;

namespace SharedKernel.Security.Oidc.Revocation;

/// <summary>
/// Decorates an inner <see cref="ITokenRevocationCheck"/> with an opt-in <see cref="IRevocationCheckCache"/>
/// seam, avoiding a revocation/introspection round-trip on every request.
/// </summary>
/// <remarks>
/// <para>
/// Checks <see cref="IRevocationCheckCache"/> first; on a cache hit, returns the cached outcome without
/// invoking the inner <see cref="ITokenRevocationCheck"/> at all. On a cache miss, calls through to the
/// inner check and populates the cache with <see cref="RevocationCheckCacheOptions.RevokedTtl"/> or
/// <see cref="RevocationCheckCacheOptions.NotRevokedTtl"/> according to the outcome.
/// </para>
/// <para>
/// Inherits the SAME fail-closed contract as the base check — a cache-lookup failure falls through to the
/// inner check, never silently treats an unreadable cache as "not revoked" (WO-060, P-388).
/// </para>
/// </remarks>
public sealed class CachingTokenRevocationCheck : ITokenRevocationCheck
{
    private readonly ITokenRevocationCheck _inner;
    private readonly IRevocationCheckCache _cache;
    private readonly RevocationCheckCacheOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="CachingTokenRevocationCheck"/>.
    /// </summary>
    /// <param name="inner">The wrapped <see cref="ITokenRevocationCheck"/> to fall back to on a cache miss.</param>
    /// <param name="cache">The consumer-supplied cache seam.</param>
    /// <param name="options">Cache TTL configuration.</param>
    public CachingTokenRevocationCheck(ITokenRevocationCheck inner, IRevocationCheckCache cache, IOptions<RevocationCheckCacheOptions> options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);

        _inner = inner;
        _cache = cache;
        _options = options.Value;
    }

    /// <inheritdoc/>
    public async Task<bool> IsRevokedAsync(string tokenIdentifier, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tokenIdentifier);

        bool? cached;
        try
        {
            cached = await _cache.TryGetAsync(tokenIdentifier, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A cache-lookup failure falls through to the inner check — it must never itself become a
            // new fail-open surface (a broken cache must not cause a legitimately non-revoked token to be
            // rejected merely because the cache could not be read), nor a new fail-closed surface (the
            // inner check's own fail-closed contract is authoritative, not this cache's availability).
            cached = null;
        }

        if (cached.HasValue)
        {
            return cached.Value;
        }

        var revoked = await _inner.IsRevokedAsync(tokenIdentifier, ct).ConfigureAwait(false);

        // RevokedTtl is DELIBERATELY SHORT (default 5s) — a "revoked" outcome is never cached beyond it,
        // so a genuine revocation is never masked by a stale cache entry for long.
        var ttl = revoked ? _options.RevokedTtl : _options.NotRevokedTtl;
        try
        {
            await _cache.SetAsync(tokenIdentifier, revoked, ttl, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed cache write must not fail the request — the inner check already produced an
            // authoritative, fail-closed-honoring result; only the (best-effort) caching of it failed.
        }

        return revoked;
    }
}
