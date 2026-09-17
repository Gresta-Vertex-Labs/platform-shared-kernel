using System.Collections.Concurrent;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Caching.Redis.HashStore;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="IRedisHashService"/> for use in unit tests.
/// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The backing store is keyed by (Redis key, field) and holds the boxed value directly.
/// <see cref="JsonTypeInfo{T}"/> parameters are accepted for signature parity only and are
/// <b>never invoked</b> — this is an in-memory fake with no wire format to cross, unlike the real
/// <c>RedisHashService</c>, which uses <see cref="JsonTypeInfo{T}"/> for genuine AOT-safe STJ
/// (de)serialization against Redis' wire bytes.
/// </para>
/// <para>
/// <see cref="IncrementFieldAsync"/> treats a missing field as <c>0</c> before adding
/// <c>delta</c>, matching Redis' own <c>HINCRBY</c> semantics. Incrementing a field that
/// currently holds a non-<see langword="long"/> value throws <see cref="InvalidCastException"/> —
/// a fake-only guard surfacing a caller bug (using <see cref="IncrementFieldAsync"/> against a
/// field populated via <see cref="SetFieldAsync{T}"/> for <c>T != long</c>), standing in for the
/// <c>WRONGTYPE</c> error the real Redis command would raise in the equivalent case.
/// </para>
/// <para>
/// <b>TEST-ONLY — NEVER PRODUCTION-SAFE.</b> This type must never be wired into a production DI
/// container — <c>16.Testing</c> packages are never referenced by production code (root
/// <c>CLAUDE.md</c> hard rule). Its silent, unenforced <see cref="JsonTypeInfo{T}"/> divergence from
/// real Redis wire behavior — the parameter is accepted but never (de)serialized against anything —
/// would silently hide a serialization bug a real <c>RedisHashService</c> would surface immediately.
/// </para>
/// </remarks>
public sealed class FakeRedisHashService : IRedisHashService
{
    private readonly ConcurrentDictionary<(string Key, string Field), object?> _store = new();

    /// <summary>
    /// When <see langword="true"/>, every member throws <see cref="InvalidOperationException"/>
    /// instead of performing the operation.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <inheritdoc />
    public ValueTask<T?> GetFieldAsync<T>(string key, string field, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(typeInfo);

        if (SimulateFailure)
            throw new InvalidOperationException("Simulated Redis hash failure.");

        if (_store.TryGetValue((key, field), out var boxed) && boxed is T typed)
            return ValueTask.FromResult<T?>(typed);

        return ValueTask.FromResult<T?>(default);
    }

    /// <inheritdoc />
    public ValueTask SetFieldAsync<T>(string key, string field, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(typeInfo);

        if (SimulateFailure)
            throw new InvalidOperationException("Simulated Redis hash failure.");

        _store[(key, field)] = value;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, T>> GetAllFieldsAsync<T>(string key, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(typeInfo);

        if (SimulateFailure)
            throw new InvalidOperationException("Simulated Redis hash failure.");

        var result = new Dictionary<string, T>();

        foreach (var ((entryKey, entryField), boxed) in _store)
        {
            if (entryKey == key && boxed is T typed)
                result[entryField] = typed;
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, T>>(result);
    }

    /// <inheritdoc />
    public ValueTask DeleteFieldAsync(string key, string field, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        if (SimulateFailure)
            throw new InvalidOperationException("Simulated Redis hash failure.");

        // Idempotent — a missing (key, field) pair is a silent no-op.
        _store.TryRemove((key, field), out _);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<long> IncrementFieldAsync(string key, string field, long delta = 1, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        if (SimulateFailure)
            throw new InvalidOperationException("Simulated Redis hash failure.");

        var updated = (long)_store.AddOrUpdate(
            (key, field),
            static (_, d) => d,
            static (entry, existing, d) => existing switch
            {
                long current => current + d,
                _ => throw new InvalidCastException(
                    $"Field '{entry.Field}' in hash '{entry.Key}' does not hold a long value and cannot be incremented."),
            },
            delta)!;

        return ValueTask.FromResult(updated);
    }

    /// <summary>
    /// Test-setup helper that pre-populates a field without going through <see cref="SetFieldAsync{T}"/>.
    /// </summary>
    /// <typeparam name="T">The field value type.</typeparam>
    /// <param name="key">The Redis hash key.</param>
    /// <param name="field">The field name within the hash.</param>
    /// <param name="value">The value to seed.</param>
    public void Seed<T>(string key, string field, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        _store[(key, field)] = value;
    }

    /// <summary>Clears every stored field across every key.</summary>
    public void Reset() => _store.Clear();
}
