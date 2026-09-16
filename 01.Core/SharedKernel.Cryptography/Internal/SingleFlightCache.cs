using System.Collections.Concurrent;

namespace SharedKernel.Cryptography.Internal;

/// <summary>
/// A bounded async cache where concurrent requests for the same key share one in-flight resolution.
/// </summary>
/// <remarks>
/// <para>
/// This file is compiled into <c>SharedKernel.Cryptography</c> and linked into
/// <c>SharedKernel.Cryptography.KeyVault.Azure</c>, so both packages use one implementation.
/// </para>
/// <para>
/// <b>Cancellation.</b> The factory runs with a token owned by the entry, never a caller's. Each caller waits with
/// its own token, so one caller cancelling never cancels another. When the last waiter leaves before the
/// resolution succeeds, the entry is marked abandoned, its work is cancelled and it is evicted; a caller that finds
/// an abandoned entry starts a new one instead of joining it.
/// </para>
/// <para>
/// <b>Bounds.</b> Failed resolutions are never kept. Successful values are kept for the time to live, unless
/// <c>shouldCache</c> rejects them. When the cache holds <c>maxEntries</c> entries, expired entries are removed; if
/// it is still full, the value is resolved without being cached, so untrusted keys cannot grow memory.
/// </para>
/// </remarks>
internal sealed class SingleFlightCache<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Entry> _entries;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan? _timeToLive;
    private readonly int _maxEntries;
    private readonly Func<TValue, bool> _shouldCache;

    public SingleFlightCache(
        TimeProvider timeProvider,
        TimeSpan? timeToLive,
        int maxEntries,
        Func<TValue, bool>? shouldCache = null,
        IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        if (timeToLive is { } ttl && ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), ttl, "The time to live must be positive.");
        }

        _entries = new ConcurrentDictionary<TKey, Entry>(comparer);
        _timeProvider = timeProvider;
        _timeToLive = timeToLive;
        _maxEntries = maxEntries;
        _shouldCache = shouldCache ?? (static _ => true);
    }

    public int Count => _entries.Count;

    public async ValueTask<TValue> GetOrAddAsync(
        TKey key,
        Func<CancellationToken, ValueTask<TValue>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);

        while (true)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            Entry? existing = null;

            if (_entries.TryGetValue(key, out Entry? found))
            {
                if (!found.IsExpired(now))
                {
                    if (found.TryAcquire())
                    {
                        return await AwaitAsync(key, found, cancellationToken).ConfigureAwait(false);
                    }

                    _entries.TryRemove(new KeyValuePair<TKey, Entry>(key, found));
                    continue;
                }

                existing = found;
            }

            if (existing is null && _entries.Count >= _maxEntries)
            {
                RemoveExpired(now);
                if (_entries.ContainsKey(key))
                {
                    // Another caller added this key after the lookup above: join it instead of bypassing the cache.
                    continue;
                }

                if (_entries.Count >= _maxEntries)
                {
                    // Full of live entries: resolve without caching rather than grow.
                    return await factory(cancellationToken).ConfigureAwait(false);
                }
            }

            var candidate = new Entry(factory, _timeToLive is { } ttl ? now + ttl : DateTimeOffset.MaxValue);
            bool stored = existing is null
                ? _entries.TryAdd(key, candidate)
                : _entries.TryUpdate(key, candidate, existing);

            if (stored && candidate.TryAcquire())
            {
                return await AwaitAsync(key, candidate, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public void Set(TKey key, TValue value)
    {
        DateTimeOffset expiresAt = _timeToLive is { } ttl ? _timeProvider.GetUtcNow() + ttl : DateTimeOffset.MaxValue;
        _entries[key] = Entry.FromValue(value, expiresAt);
    }

    public void Remove(TKey key) => _entries.TryRemove(key, out _);

    private async ValueTask<TValue> AwaitAsync(TKey key, Entry entry, CancellationToken cancellationToken)
    {
        try
        {
            TValue value = await entry.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!_shouldCache(value))
            {
                _entries.TryRemove(new KeyValuePair<TKey, Entry>(key, entry));
            }

            return value;
        }
        finally
        {
            if (entry.Release())
            {
                _entries.TryRemove(new KeyValuePair<TKey, Entry>(key, entry));
            }
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (KeyValuePair<TKey, Entry> pair in _entries)
        {
            if (pair.Value.IsExpired(now))
            {
                _entries.TryRemove(pair);
            }
        }
    }

    private sealed class Entry
    {
        private readonly Lock _gate = new();
        private readonly CancellationTokenSource? _cancellation;
        private readonly Lazy<Task<TValue>> _task;
        private readonly DateTimeOffset _expiresAt;
        private int _waiters;
        private bool _abandoned;

        public Entry(Func<CancellationToken, ValueTask<TValue>> factory, DateTimeOffset expiresAt)
        {
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;
            _task = new Lazy<Task<TValue>>(() => RunAsync(factory, token), LazyThreadSafetyMode.ExecutionAndPublication);
            _expiresAt = expiresAt;
        }

        private Entry(TValue value, DateTimeOffset expiresAt)
        {
            _task = new Lazy<Task<TValue>>(System.Threading.Tasks.Task.FromResult(value));
            _expiresAt = expiresAt;
        }

        public Task<TValue> Task => _task.Value;

        public static Entry FromValue(TValue value, DateTimeOffset expiresAt) => new(value, expiresAt);

        // An async wrapper turns a factory that throws synchronously into a faulted task, which Lazy<T> would
        // otherwise cache and rethrow from every access.
        private static async Task<TValue> RunAsync(Func<CancellationToken, ValueTask<TValue>> factory, CancellationToken token) =>
            await factory(token).ConfigureAwait(false);

        public bool IsExpired(DateTimeOffset now) => now >= _expiresAt;

        public bool TryAcquire()
        {
            lock (_gate)
            {
                if (_abandoned)
                {
                    return false;
                }

                _waiters++;
                return true;
            }
        }

        /// <summary>Releases one waiter. Returns <see langword="true"/> when the entry was abandoned and must be evicted.</summary>
        public bool Release()
        {
            lock (_gate)
            {
                _waiters--;
                if (_waiters > 0 || (_task.IsValueCreated && _task.Value.IsCompletedSuccessfully))
                {
                    return false;
                }

                _abandoned = true;
            }

            _cancellation?.Cancel();
            return true;
        }
    }
}
