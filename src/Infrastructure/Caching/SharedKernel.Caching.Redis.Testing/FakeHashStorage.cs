namespace SharedKernel.Testing.Caching;

/// <summary>
/// The in-memory hash store shared in shape (not in state) by <see cref="FakeRedisHashService"/> and
/// <see cref="FakeTypedHashStore{T}"/>: hashes by key, fields by name, and a per-key expiry read from a
/// <see cref="TimeProvider"/>.
/// </summary>
/// <remarks>
/// Mirrors the Redis rules the real hash store relies on: a write with a time-to-live sets the expiry of the
/// whole key together with the fields; a key past its expiry is gone; a hash whose last field is removed no
/// longer exists; <c>PERSIST</c> reports a change only when the key had an expiry. Every member takes one lock,
/// so each operation is atomic, as the Redis commands and <c>MULTI</c>/<c>EXEC</c> blocks it stands in for are.
/// </remarks>
internal sealed class FakeHashStorage(TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Hash> _hashes = new(StringComparer.Ordinal);

    public TimeProvider TimeProvider { get; } = timeProvider;

    public bool TryGetField(string key, string field, out object? value)
    {
        lock (_gate)
        {
            value = null;
            return TryGetLive(key, out var hash) && hash.Fields.TryGetValue(field, out value);
        }
    }

    public List<KeyValuePair<string, object?>> GetFields(string key, IReadOnlyList<string> fields)
    {
        lock (_gate)
        {
            var result = new List<KeyValuePair<string, object?>>(fields.Count);
            if (!TryGetLive(key, out var hash))
                return result;

            foreach (var field in fields)
            {
                if (hash.Fields.TryGetValue(field, out var value))
                    result.Add(new KeyValuePair<string, object?>(field, value));
            }

            return result;
        }
    }

    public List<KeyValuePair<string, object?>> GetAll(string key)
    {
        lock (_gate)
        {
            return TryGetLive(key, out var hash) ? [.. hash.Fields] : [];
        }
    }

    public void Set(string key, IReadOnlyList<KeyValuePair<string, object?>> entries, TimeSpan? timeToLive)
    {
        lock (_gate)
        {
            var hash = GetOrCreate(key);
            foreach (var (field, value) in entries)
                hash.Fields[field] = value;

            if (timeToLive is { } ttl)
                hash.ExpiresAt = TimeProvider.GetUtcNow() + ttl;
        }
    }

    public long Increment(string key, string field, long delta, TimeSpan? timeToLive, Func<object?, long> read, Func<long, object?> write)
    {
        lock (_gate)
        {
            TryGetLive(key, out var existing);
            var current = existing is not null && existing.Fields.TryGetValue(field, out var value) ? read(value) : 0L;

            long updated;
            try
            {
                updated = checked(current + delta);
            }
            catch (OverflowException)
            {
                // Redis answers "ERR increment or decrement would overflow" and leaves the field unchanged.
                throw new InvalidOperationException(
                    $"Incrementing field '{field}' of hash '{key}' by {delta} would overflow a 64-bit integer.");
            }

            var hash = existing ?? GetOrCreate(key);
            hash.Fields[field] = write(updated);
            if (timeToLive is { } ttl)
                hash.ExpiresAt = TimeProvider.GetUtcNow() + ttl;

            return updated;
        }
    }

    public bool DeleteField(string key, string field)
    {
        lock (_gate)
        {
            if (!TryGetLive(key, out var hash) || !hash.Fields.Remove(field))
                return false;

            // Redis removes a hash whose last field is deleted, expiry included.
            if (hash.Fields.Count == 0)
                _hashes.Remove(key);

            return true;
        }
    }

    public bool Delete(string key)
    {
        lock (_gate)
        {
            return TryGetLive(key, out _) && _hashes.Remove(key);
        }
    }

    public bool Expire(string key, TimeSpan? timeToLive)
    {
        lock (_gate)
        {
            if (!TryGetLive(key, out var hash))
                return false;

            if (timeToLive is { } ttl)
            {
                hash.ExpiresAt = TimeProvider.GetUtcNow() + ttl;
                return true;
            }

            // PERSIST: true only when an expiry was actually removed.
            if (hash.ExpiresAt is null)
                return false;

            hash.ExpiresAt = null;
            return true;
        }
    }

    public bool ContainsKey(string key)
    {
        lock (_gate)
        {
            return TryGetLive(key, out _);
        }
    }

    public TimeSpan? GetTimeToLive(string key)
    {
        lock (_gate)
        {
            return TryGetLive(key, out var hash) && hash.ExpiresAt is { } expiresAt
                ? expiresAt - TimeProvider.GetUtcNow()
                : null;
        }
    }

    public IReadOnlyList<string> GetKeys()
    {
        lock (_gate)
        {
            var now = TimeProvider.GetUtcNow();
            return [.. _hashes.Where(pair => !IsExpired(pair.Value, now)).Select(pair => pair.Key)];
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _hashes.Clear();
        }
    }

    public static void ValidateKeyAndField(string key, string field)
    {
        ValidateKey(key);
        ValidateField(field);
    }

    public static void ValidateKey(string key) => ArgumentException.ThrowIfNullOrWhiteSpace(key);

    public static void ValidateField(string field) => ArgumentException.ThrowIfNullOrEmpty(field);

    public static void ValidateTimeToLive(TimeSpan? timeToLive)
    {
        if (timeToLive is { } ttl)
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero, nameof(timeToLive));
    }

    private bool TryGetLive(string key, out Hash hash)
    {
        if (!_hashes.TryGetValue(key, out hash!))
            return false;

        if (!IsExpired(hash, TimeProvider.GetUtcNow()))
            return true;

        _hashes.Remove(key);
        hash = null!;
        return false;
    }

    private Hash GetOrCreate(string key)
    {
        if (TryGetLive(key, out var hash))
            return hash;

        hash = new Hash();
        _hashes[key] = hash;
        return hash;
    }

    private static bool IsExpired(Hash hash, DateTimeOffset now) => hash.ExpiresAt is { } expiresAt && now >= expiresAt;

    private sealed class Hash
    {
        public Dictionary<string, object?> Fields { get; } = new(StringComparer.Ordinal);

        public DateTimeOffset? ExpiresAt { get; set; }
    }
}
