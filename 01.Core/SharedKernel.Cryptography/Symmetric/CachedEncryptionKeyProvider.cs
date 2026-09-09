using System.Collections.Concurrent;

namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// A bounded-TTL, single-flight caching decorator over an inner <see cref="IEncryptionKeyProvider"/>
/// — avoids re-resolving key material (e.g. a network-bound KMS call) on every
/// <see cref="ISymmetricEncryptionService"/> operation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never serves an entry past its configured TTL.</b> A cache hit inside the TTL window never
/// calls the inner provider; an expired (or missing) entry always re-fetches from the inner
/// provider before returning. There is no stale-serve-while-revalidating behavior anywhere in
/// this type.
/// </para>
/// <para>
/// <b>Single-flight per cache key.</b> When N callers concurrently request the same cache key
/// (the current key, or one specific <c>keyId</c>) past expiry, exactly one call reaches the
/// inner provider — every other caller awaits that same in-flight resolution instead of issuing
/// its own redundant call. This avoids a cache-stampede thundering herd against the inner
/// provider (e.g. a KMS) at expiry.
/// </para>
/// <para>
/// <b>Fail-closed on refresh failure.</b> If the inner provider's call during a refresh throws,
/// that exception propagates to every caller awaiting the single-flight resolution — this type
/// NEVER falls back to a stale cached value on a failed refresh. The failed slot is discarded so
/// the next call attempts a fresh resolution rather than being permanently poisoned.
/// </para>
/// <para>
/// Ships with <b>no package-owned DI extension</b> — mirrors the <c>IIdGenerator</c>/
/// <c>SystemClock(TimeProvider)</c> no-extension precedent. Compose it explicitly at the
/// consumer's own composition root:
/// <c>services.AddSingleton&lt;IEncryptionKeyProvider&gt;(sp =&gt; new CachedEncryptionKeyProvider(new MyKmsProvider(...), TimeProvider.System, ttl));</c>
/// </para>
/// </remarks>
public sealed class CachedEncryptionKeyProvider : IEncryptionKeyProvider
{
    // A sentinel cache key for GetCurrentKeyAsync, chosen so it can never collide with a real
    // caller-supplied keyId (keyId values are opaque strings owned by the inner provider, but a
    // colon-prefixed sentinel with no valid keyId shape is used as an extra guard, matched by the
    // same prefixing applied to per-keyId cache entries below).
    private const string CurrentKeyCacheKey = "current:";
    private const string KeyIdCacheKeyPrefix = "id:";

    private readonly IEncryptionKeyProvider _inner;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, CacheSlot> _slots = new();

    /// <summary>
    /// The wrapped provider whose results this decorator caches.
    /// </summary>
    /// <remarks>
    /// Exposed so <see cref="EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(IEncryptionKeyProvider)"/>
    /// can see through this decorator: a <see cref="CachedEncryptionKeyProvider"/> never directly
    /// implements <see cref="ISynchronousEncryptionKeyProvider"/> itself — a cache hit is fast
    /// regardless of what is wrapped, but a cache miss re-enters <see cref="Inner"/>, so this
    /// type's true synchronous-safety is entirely a function of what it wraps.
    /// </remarks>
    public IEncryptionKeyProvider Inner => _inner;

    /// <summary>Creates a new <see cref="CachedEncryptionKeyProvider"/>.</summary>
    /// <param name="inner">The provider whose results are cached.</param>
    /// <param name="timeProvider">The time source used to compute cache-entry expiry.</param>
    /// <param name="ttl">
    /// How long a resolved key is served from cache before the next request triggers a fresh
    /// resolution from <paramref name="inner"/>. Must be a positive duration.
    /// </param>
    public CachedEncryptionKeyProvider(IEncryptionKeyProvider inner, TimeProvider timeProvider, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "The cache TTL must be a positive duration.");
        }

        _inner = inner;
        _timeProvider = timeProvider;
        _ttl = ttl;
    }

    /// <inheritdoc />
    public async ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default)
    {
        CryptographicKey? key = await GetOrRefreshAsync(
                CurrentKeyCacheKey,
                async innerCt => await _inner.GetCurrentKeyAsync(innerCt).ConfigureAwait(false),
                ct)
            .ConfigureAwait(false);

        // GetCurrentKeyAsync never legitimately returns null per the IEncryptionKeyProvider
        // contract — a well-behaved inner provider either returns a key or throws.
        return key!;
    }

    /// <inheritdoc />
    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        return GetOrRefreshAsync(
            KeyIdCacheKeyPrefix + keyId,
            innerCt => _inner.GetKeyAsync(keyId, innerCt),
            ct);
    }

    /// <summary>
    /// Resolves <paramref name="cacheKey"/> from cache when unexpired, or performs a
    /// single-flight refresh via <paramref name="factory"/> otherwise. Multiple concurrent
    /// callers past expiry are guaranteed to observe exactly one call to
    /// <paramref name="factory"/>, via an atomic compare-and-swap race over the backing
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> combined with a <see cref="Lazy{T}"/>
    /// whose factory delegate itself executes at most once even under contention.
    /// </summary>
    /// <remarks>
    /// <b>
    /// CANCELLATION (P-511/WO-083): THE SHARED IN-FLIGHT REFRESH IS DRIVEN BY A
    /// <see cref="CacheSlot"/>-OWNED <see cref="CancellationTokenSource"/> — NEVER BY ANY
    /// INDIVIDUAL CALLER'S <paramref name="ct"/>. EACH CALLER OBSERVES ONLY ITS OWN TOKEN VIA
    /// <see cref="Task.WaitAsync(CancellationToken)"/>, SO ONE CALLER CANCELLING ITS OWN AWAIT
    /// CAN NEVER CANCEL OR FAULT ANOTHER CALLER'S CONCURRENT AWAIT OF THE SAME SHARED
    /// RESOLUTION. THE SLOT'S OWNED SOURCE IS CANCELLED — GENUINELY ABANDONING THE INNER
    /// <paramref name="factory"/> CALL — ONLY ONCE EVERY CALLER CURRENTLY AWAITING THIS SLOT HAS
    /// DEPARTED (A PER-SLOT REFERENCE COUNT REACHING ZERO), NEVER WHILE AT LEAST ONE CALLER IS
    /// STILL LEGITIMATELY WAITING.
    /// </b>
    /// </remarks>
    private async ValueTask<CryptographicKey?> GetOrRefreshAsync(
        string cacheKey,
        Func<CancellationToken, ValueTask<CryptographicKey?>> factory,
        CancellationToken ct)
    {
        while (true)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();

            if (_slots.TryGetValue(cacheKey, out CacheSlot? current) && now < current.ExpiresAt)
            {
                return await current.AwaitAsync(ct, () => EvictIfCurrent(cacheKey, current)).ConfigureAwait(false);
            }

            var candidate = new CacheSlot(factory, now + _ttl);

            CacheSlot winner;
            if (current is null)
            {
                // No entry existed at all — GetOrAdd is atomic for storage: every concurrent
                // caller racing here receives the SAME stored slot back, even though each may
                // have constructed its own (cheap, inert-until-.Value) candidate.
                winner = _slots.GetOrAdd(cacheKey, candidate);
            }
            else if (_slots.TryUpdate(cacheKey, candidate, current))
            {
                // Won the compare-and-swap replacing the known-expired entry.
                winner = candidate;
            }
            else
            {
                // Another caller already replaced the expired entry — retry and pick up
                // whatever is there now instead of proceeding with our own stale "current".
                continue;
            }

            return await winner.AwaitAsync(ct, () => EvictIfCurrent(cacheKey, winner)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Removes <paramref name="slot"/> from <see cref="_slots"/>, but only if it is still the
    /// entry stored under <paramref name="cacheKey"/> — a subsequent successful refresh may
    /// already have replaced it, in which case this is a correct no-op.
    /// </summary>
    private void EvictIfCurrent(string cacheKey, CacheSlot slot)
        => _slots.TryRemove(new KeyValuePair<string, CacheSlot>(cacheKey, slot));

    /// <summary>
    /// A single cache entry backing a single-flight, cross-caller-cancellation-safe refresh. The
    /// underlying <see cref="Task{TResult}"/> is created and driven by a
    /// <see cref="CancellationTokenSource"/> owned by this slot — never derived from, linked to,
    /// or constructed from any individual caller's token — so exactly one caller's cancellation
    /// can never disturb another caller's concurrent await of the same in-flight resolution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>
    /// CANCELLATION (P-511/WO-083 — the fully-documented reference implementation of this pattern;
    /// see <c>AzureKeyVaultEncryptionKeyProvider</c> and <c>AzureKeyVaultAsymmetricKeyProvider</c>
    /// in <c>SharedKernel.Cryptography.KeyVault.Azure</c> for the two other classes (three sites)
    /// applying the identical shape). EACH CALLER AWAITS THE SHARED <see cref="Task{TResult}"/> VIA
    /// <see cref="Task.WaitAsync(CancellationToken)"/> WITH ITS OWN TOKEN — <see cref="Task.WaitAsync(CancellationToken)"/>
    /// NEVER CANCELS THE ORIGINAL TASK, IT ONLY STOPS *THIS CALLER* FROM WAITING ON IT FURTHER. ONE
    /// CALLER'S OWN CANCELLATION THEREFORE CAN NEVER CANCEL OR FAULT ANOTHER CALLER'S CONCURRENT
    /// AWAIT OF THE SAME SHARED RESOLUTION.
    /// </b>
    /// </para>
    /// <para>
    /// A <c>waiterCount</c> reference count, incremented before each <see cref="Task.WaitAsync(CancellationToken)"/>
    /// and decremented in a <c>finally</c> once that caller's own wait completes (success, fault, or
    /// that caller's own cancellation), tracks how many callers are currently awaiting this slot.
    /// Only when this count reaches zero — every caller has departed — is the slot's own
    /// <see cref="CancellationTokenSource"/> cancelled, genuinely abandoning the inner factory call
    /// if it is still running. If, at that same moment, the shared task has NOT already completed
    /// successfully, the caller that brought the count to zero also evicts this slot from the
    /// owning cache (via the supplied <c>onLastWaiterLeavesUnsuccessfully</c> callback) — a doomed
    /// (about-to-be-cancelled) or already-faulted slot must never be handed to a future caller that
    /// arrives after everyone else has abandoned it, while a genuinely successful result is never
    /// evicted just because its waiters happened to all leave (it stays cached for its full TTL, as
    /// designed). Cancelling the owned source after the shared task has already completed
    /// successfully is a harmless no-op — it can never disturb an already-produced result, so the
    /// ordering race between "last caller leaves" and "operation completes" is safe by construction.
    /// </para>
    /// </remarks>
    private sealed class CacheSlot
    {
        private readonly Lazy<Task<CryptographicKey?>> _fetch;
        private readonly CancellationTokenSource _ownedCts = new();
        private int _waiterCount;

        public CacheSlot(Func<CancellationToken, ValueTask<CryptographicKey?>> factory, DateTimeOffset expiresAt)
        {
            ExpiresAt = expiresAt;
            _fetch = new Lazy<Task<CryptographicKey?>>(
                () => factory(_ownedCts.Token).AsTask(),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public DateTimeOffset ExpiresAt { get; }

        /// <summary>
        /// Awaits this slot's shared, single-flight resolution. See the type-level CANCELLATION
        /// remarks for the full cross-caller-cancellation-safety and eviction contract.
        /// </summary>
        /// <param name="callerCt">This specific caller's own cancellation token.</param>
        /// <param name="onLastWaiterLeavesUnsuccessfully">
        /// Invoked when this caller is the last to depart this slot AND the shared resolution has
        /// not completed successfully (still pending — now abandoned — or already faulted). Never
        /// invoked when the shared resolution already completed successfully.
        /// </param>
        public async Task<CryptographicKey?> AwaitAsync(
            CancellationToken callerCt,
            Action onLastWaiterLeavesUnsuccessfully)
        {
            Interlocked.Increment(ref _waiterCount);
            try
            {
                return await _fetch.Value.WaitAsync(callerCt).ConfigureAwait(false);
            }
            finally
            {
                if (Interlocked.Decrement(ref _waiterCount) == 0)
                {
                    _ownedCts.Cancel();
                    if (!_fetch.Value.IsCompletedSuccessfully)
                    {
                        onLastWaiterLeavesUnsuccessfully();
                    }
                }
            }
        }
    }
}
