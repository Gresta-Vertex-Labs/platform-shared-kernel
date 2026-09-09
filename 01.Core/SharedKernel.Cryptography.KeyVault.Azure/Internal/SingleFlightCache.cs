using System.Collections.Concurrent;

namespace SharedKernel.Cryptography.KeyVault.Azure.Internal;

/// <summary>
/// A single-flight, cross-caller-cancellation-safe async resolution cache with no expiry of its
/// own — used internally by this package's three cache sites
/// (<see cref="AzureKeyVaultEncryptionKeyProvider"/>'s <c>_resolvedKeysByCacheKey</c> and
/// <c>_plaintextKeysByTag</c>, and <see cref="AzureKeyVaultAsymmetricKeyProvider"/>'s
/// <c>_resolvedKeysByAzureKeyName</c>) so all three apply the IDENTICAL cross-caller-cancellation
/// fix (P-511/WO-083) instead of three independently hand-rolled, drift-prone copies.
/// </summary>
/// <remarks>
/// <para>
/// <b>
/// CANCELLATION (P-511/WO-083 — see <c>SharedKernel.Cryptography.Symmetric.CachedEncryptionKeyProvider</c>'s
/// own <c>CacheSlot</c> for the fully-documented reference explanation of this exact pattern; this
/// type is the identical shape generalized for reuse across this package's three sites). Each
/// caller awaits the shared in-flight <see cref="Task{TResult}"/> via
/// <see cref="Task.WaitAsync(CancellationToken)"/> using its OWN token — <see cref="Task.WaitAsync(CancellationToken)"/>
/// never cancels the original task, it only stops that one caller from waiting on it further, so
/// one caller's own cancellation can never cancel or fault another caller's concurrent await of the
/// same shared resolution. The shared factory delegate is driven by a cache-slot-owned
/// <see cref="CancellationTokenSource"/> — never derived from, linked to, or constructed from any
/// individual caller's token — cancelled only once every currently-awaiting caller has departed (a
/// per-slot reference count reaching zero), genuinely abandoning the inner work once nobody remains
/// interested, never while at least one caller is still legitimately waiting.
/// </b>
/// </para>
/// <para>
/// A slot whose shared resolution has not completed successfully by the moment its last waiter
/// departs (still pending — now abandoned — or already faulted) is evicted from the cache
/// immediately, so a future caller arriving after everyone else has abandoned it never joins a
/// doomed, about-to-be-cancelled slot and never observes a permanently-poisoned failure. A slot
/// that DID complete successfully is never evicted just because its waiters happened to all leave —
/// cancelling the owned source after a successful completion is a harmless no-op, so the ordering
/// race between "last caller leaves" and "operation completes" is safe by construction.
/// </para>
/// </remarks>
/// <typeparam name="TKey">The cache key type.</typeparam>
/// <typeparam name="TValue">The resolved value type.</typeparam>
internal sealed class SingleFlightCache<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Slot> _slots;

    public SingleFlightCache(IEqualityComparer<TKey>? comparer = null)
        => _slots = comparer is null ? new ConcurrentDictionary<TKey, Slot>() : new ConcurrentDictionary<TKey, Slot>(comparer);

    /// <summary>
    /// Resolves <paramref name="key"/> via its existing in-flight or previously-succeeded slot, or
    /// starts a new single-flight resolution via <paramref name="factory"/> when no slot currently
    /// exists. <paramref name="factory"/> is invoked with a cache-slot-owned token, never
    /// <paramref name="callerCt"/> — see the type-level CANCELLATION remarks.
    /// </summary>
    public Task<TValue> GetOrAddAsync(TKey key, Func<CancellationToken, Task<TValue>> factory, CancellationToken callerCt)
    {
        Slot slot = _slots.GetOrAdd(key, static (_, f) => new Slot(f), factory);
        return slot.AwaitAsync(callerCt, () => EvictIfCurrent(key, slot));
    }

    /// <summary>
    /// Directly seeds <paramref name="key"/> with an already-known <paramref name="value"/> —
    /// e.g. a caller that just minted a value locally and wants to memoize it without forcing a
    /// redundant round trip through <see cref="GetOrAddAsync"/>'s factory path. Overwrites any
    /// existing slot for <paramref name="key"/> unconditionally.
    /// </summary>
    public void Seed(TKey key, TValue value)
        => _slots[key] = Slot.FromResult(value);

    private void EvictIfCurrent(TKey key, Slot slot)
        => _slots.TryRemove(new KeyValuePair<TKey, Slot>(key, slot));

    private sealed class Slot
    {
        private readonly Lazy<Task<TValue>> _fetch;
        private readonly CancellationTokenSource? _ownedCts;
        private int _waiterCount;

        public Slot(Func<CancellationToken, Task<TValue>> factory)
        {
            _ownedCts = new CancellationTokenSource();
            _fetch = new Lazy<Task<TValue>>(
                () => factory(_ownedCts.Token),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        private Slot(Task<TValue> completed)
        {
            _ownedCts = null;
            _fetch = new Lazy<Task<TValue>>(completed);
        }

        public static Slot FromResult(TValue value) => new(Task.FromResult(value));

        public async Task<TValue> AwaitAsync(CancellationToken callerCt, Action onLastWaiterLeavesUnsuccessfully)
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
                    _ownedCts?.Cancel();
                    if (!_fetch.Value.IsCompletedSuccessfully)
                    {
                        onLastWaiterLeavesUnsuccessfully();
                    }
                }
            }
        }
    }
}
