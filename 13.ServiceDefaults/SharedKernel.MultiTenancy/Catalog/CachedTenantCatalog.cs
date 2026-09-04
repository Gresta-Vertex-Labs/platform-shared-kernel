using System.Collections.Concurrent;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.MultiTenancy.Catalog;

/// <summary>
/// A short, bounded-TTL, in-memory caching decorator over any <see cref="ITenantCatalog"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A BOUNDED, SHORT DEFAULT TTL — NOT A PERF TRADEOFF.</b> Mirrors
/// <c>K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds</c>'s 30-second-default reasoning. AN
/// UNBOUNDED OR PURELY-TTL-BASED CACHE LETS A SUSPENDED TENANT KEEP OPERATING UNTIL THE TTL
/// EXPIRES — A CORRECTNESS BUG FOR A FINTECH-GRADE PLATFORM. <see cref="InvalidateTenantAsync"/>
/// exists precisely so a consuming service can force immediate re-resolution the moment it
/// changes a tenant's status, rather than relying on the TTL alone.
/// </para>
/// <para>
/// <see cref="CatalogTenantStatusValidator"/> composes with this type with <b>zero code
/// changes</b> — it depends only on the <see cref="ITenantCatalog"/> interface, never a concrete
/// implementation type: <c>new CatalogTenantStatusValidator(new CachedTenantCatalog(inner))</c>
/// works exactly as if <c>inner</c> were passed directly.
/// </para>
/// <para>
/// <b>Opt-in cross-instance invalidation.</b> <see cref="WithCrossInstanceInvalidation"/> wires a
/// <c>02.Caching.Abstractions</c> <see cref="ICacheInvalidationBus"/> to <i>publish</i> a signal on
/// every local <see cref="InvalidateTenantAsync"/> call — this is a direct, compiled reference to
/// <c>02.Caching.Abstractions</c> only (never the concrete <c>.Redis.PubSub</c> provider), since
/// <c>13.ServiceDefaults</c>'s layering ceiling already legally covers <c>02.Caching</c>. A
/// consumer who never calls this method pulls in no <see cref="ICacheInvalidationBus"/> DI
/// registration and, since only the interface is referenced, no Redis surface at all.
/// </para>
/// <para>
/// <b>Receiving a signal from another replica is a separate step, by design.</b>
/// <see cref="ICacheInvalidationBus"/> itself exposes <i>publish</i> operations only — it has no
/// subscribe/receive surface (receiving is internal plumbing owned by <c>02.Caching</c>'s own
/// <c>ICacheService</c> wiring, not exposed generically). So the "subscribe" half of cross-instance
/// invalidation is: the consuming service wires its own Redis Pub/Sub subscription (e.g.
/// <c>IRedisChannelService.SubscribeAsync</c>, set up once at its own composition root) and, on
/// receipt of a signal for this catalog's channel, calls
/// <see cref="HandleCrossInstanceInvalidationSignal"/> — a public, side-effect-free-beyond-the-cache
/// entry point that removes the local entry WITHOUT re-publishing (avoiding an infinite
/// publish/receive echo across replicas).
/// </para>
/// </remarks>
public sealed class CachedTenantCatalog : ITenantCatalog
{
    /// <summary>
    /// The default cache TTL: 30 seconds. Short and bounded deliberately — see the type-level
    /// remarks.
    /// </summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

    private readonly ITenantCatalog _inner;
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, CacheEntry> _byId = new();
    private readonly ConcurrentDictionary<string, CacheEntry> _byResolutionKey = new();
    private ICacheInvalidationBus? _invalidationBus;

    /// <summary>
    /// Creates a new <see cref="CachedTenantCatalog"/> wrapping <paramref name="inner"/>.
    /// </summary>
    /// <param name="inner">The <see cref="ITenantCatalog"/> to cache lookups from.</param>
    /// <param name="ttl">
    /// The cache TTL. Defaults to <see cref="DefaultTtl"/> (30 seconds) when omitted. Must be
    /// positive.
    /// </param>
    /// <param name="timeProvider">
    /// The <see cref="TimeProvider"/> used to evaluate TTL expiry. Defaults to
    /// <see cref="TimeProvider.System"/> — inject a fake for deterministic tests.
    /// </param>
    public CachedTenantCatalog(ITenantCatalog inner, TimeSpan? ttl = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inner);

        var resolvedTtl = ttl ?? DefaultTtl;
        if (resolvedTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), resolvedTtl, "The cache TTL must be positive.");
        }

        _inner = inner;
        _ttl = resolvedTtl;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task<TenantDescriptor?> GetByIdAsync(Guid tenantId, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        if (_byId.TryGetValue(tenantId, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Value;
        }

        var descriptor = await _inner.GetByIdAsync(tenantId, ct).ConfigureAwait(false);
        _byId[tenantId] = new CacheEntry(descriptor, now + _ttl);

        return descriptor;
    }

    /// <inheritdoc/>
    public async Task<TenantDescriptor?> GetByResolutionKeyAsync(string resolutionKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resolutionKey);

        var now = _timeProvider.GetUtcNow();
        if (_byResolutionKey.TryGetValue(resolutionKey, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Value;
        }

        var descriptor = await _inner.GetByResolutionKeyAsync(resolutionKey, ct).ConfigureAwait(false);
        _byResolutionKey[resolutionKey] = new CacheEntry(descriptor, now + _ttl);

        return descriptor;
    }

    /// <summary>
    /// Removes any cached entry for <paramref name="tenantId"/> immediately, bypassing the TTL
    /// entirely. Call this right after changing a tenant's status.
    /// </summary>
    /// <remarks>
    /// Also publishes a cross-instance invalidation signal when
    /// <see cref="WithCrossInstanceInvalidation"/> has been called.
    /// </remarks>
    /// <param name="tenantId">The tenant identifier to evict from the cache.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task InvalidateTenantAsync(Guid tenantId, CancellationToken ct)
    {
        RemoveLocal(tenantId);

        if (_invalidationBus is { } bus)
        {
            await bus.PublishKeyInvalidationAsync([BuildBusKey(tenantId)], ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opts this catalog into publishing an invalidation signal (via <paramref name="bus"/>) on
    /// every local <see cref="InvalidateTenantAsync"/> call, so peer replicas can evict their own
    /// cached copy of the same tenant. Disabled by default.
    /// </summary>
    /// <remarks>
    /// See the type-level remarks for why <i>receiving</i> a signal from another replica is a
    /// separate consumer-side wiring step (<see cref="HandleCrossInstanceInvalidationSignal"/>) —
    /// <see cref="ICacheInvalidationBus"/> itself has no subscribe surface.
    /// </remarks>
    /// <param name="bus">The cross-instance invalidation bus to publish through.</param>
    /// <returns>The same <see cref="CachedTenantCatalog"/> instance, for fluent chaining.</returns>
    public CachedTenantCatalog WithCrossInstanceInvalidation(ICacheInvalidationBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);

        _invalidationBus = bus;
        return this;
    }

    /// <summary>
    /// Removes any cached entry for <paramref name="tenantId"/>, in response to a cross-instance
    /// invalidation signal received from another replica. Never re-publishes — calling this from
    /// a consumer's own bus-receive callback does not echo the signal back onto the bus.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to evict from the local cache.</param>
    public void HandleCrossInstanceInvalidationSignal(Guid tenantId) => RemoveLocal(tenantId);

    private void RemoveLocal(Guid tenantId)
    {
        _byId.TryRemove(tenantId, out _);

        // The resolution-key cache is keyed by an opaque string this type never derives a
        // tenant id from directly, so a targeted invalidation prunes any entry whose cached
        // descriptor matches the invalidated tenant. A negative (null) cached entry cannot be
        // matched this way and is left to expire naturally via the TTL — an acceptable tradeoff
        // given how short that TTL is by design.
        foreach (var (key, entry) in _byResolutionKey)
        {
            if (entry.Value?.TenantId == tenantId)
            {
                _byResolutionKey.TryRemove(key, out _);
            }
        }
    }

    private static string BuildBusKey(Guid tenantId) => $"tenant-catalog:{tenantId:D}";

    private readonly record struct CacheEntry(TenantDescriptor? Value, DateTimeOffset ExpiresAt);
}
