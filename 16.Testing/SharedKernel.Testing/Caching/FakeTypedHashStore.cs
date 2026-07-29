using System.Collections.Concurrent;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="ITypedHashStore{T}"/> for use in unit tests.
/// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
/// <remarks>
/// <para>
/// An <b>independent</b> fake with its own backing store — deliberately not a thin wrapper
/// composing <see cref="FakeRedisHashService"/> internally, mirroring this folder's established
/// convention that its fakes share no state with each other. Same missing-field-defaults, type
/// filtering, idempotent-delete, and increment/<see cref="InvalidCastException"/>-on-type-mismatch
/// semantics as <see cref="FakeRedisHashService"/>, minus the
/// <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/> parameter — matching
/// <see cref="ITypedHashStore{T}"/>'s own purpose as the AOT-safe, no-per-call-type-info wrapper.
/// </para>
/// <para>
/// Register one <see cref="FakeTypedHashStore{T}"/> per DTO type, exactly like the real
/// <c>AddTypedHashStore&lt;T&gt;(JsonTypeInfo&lt;T&gt;)</c> convention.
/// </para>
/// <para>
/// <b>TEST-ONLY — NEVER PRODUCTION-SAFE.</b> This type must never be wired into a production DI
/// container — <c>16.Testing</c> packages are never referenced by production code (root
/// <c>CLAUDE.md</c> hard rule). Unlike <see cref="FakeRedisHashService"/>, it never accepts a
/// <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/> parameter at all — its
/// backing store still holds boxed values directly rather than serialized wire bytes, so it never
/// exercises the AOT-safe serialization path <see cref="ITypedHashStore{T}"/> exists to wrap.
/// </para>
/// </remarks>
/// <typeparam name="T">The DTO type stored in the fake hash.</typeparam>
public sealed class FakeTypedHashStore<T> : ITypedHashStore<T>
{
    private readonly ConcurrentDictionary<(string Key, string Field), object?> _store = new();

    /// <summary>
    /// When <see langword="true"/>, every member throws <see cref="InvalidOperationException"/>
    /// instead of performing the operation.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <inheritdoc />
    public ValueTask<T?> GetFieldAsync(string key, string field, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        if (SimulateFailure)
            throw new InvalidOperationException("Simulated Redis hash failure.");

        if (_store.TryGetValue((key, field), out var boxed) && boxed is T typed)
            return ValueTask.FromResult<T?>(typed);

        return ValueTask.FromResult<T?>(default);
    }

    /// <inheritdoc />
    public ValueTask SetFieldAsync(string key, string field, T value, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        if (SimulateFailure)
            throw new InvalidOperationException("Simulated Redis hash failure.");

        _store[(key, field)] = value;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, T>> GetAllFieldsAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

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
    /// Test-setup helper that pre-populates a field without going through
    /// <see cref="SetFieldAsync"/>.
    /// </summary>
    /// <param name="key">The Redis key of the hash.</param>
    /// <param name="field">The field name within the hash.</param>
    /// <param name="value">The value to seed.</param>
    public void Seed(string key, string field, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        _store[(key, field)] = value;
    }

    /// <summary>Clears every stored field across every key.</summary>
    public void Reset() => _store.Clear();
}
