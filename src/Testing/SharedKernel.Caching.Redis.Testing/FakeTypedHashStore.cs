using System.Text.Json;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.HashStore;
using static SharedKernel.Testing.Caching.FakeHashStorage;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="ITypedHashStore{T}"/> for use in unit tests. Thread-safe.
/// </summary>
/// <typeparam name="T">The type of every field value.</typeparam>
/// <remarks>
/// <para>
/// An independent fake with its own storage, not a wrapper over <see cref="FakeRedisHashService"/>. Values are
/// held as <typeparamref name="T"/> instances, not serialized, because the store has no JSON contract; key,
/// field, time-to-live and expiry behave exactly as in <see cref="FakeRedisHashService"/>, including argument
/// validation and the <see cref="TimeProvider"/> passed to the constructor.
/// </para>
/// <para>
/// <see cref="IncrementFieldAsync"/> keeps counters as <see cref="long"/>. It fails with
/// <see cref="InvalidOperationException"/> on a field holding anything else, and reading a counter from a
/// store whose <typeparamref name="T"/> is not <see cref="long"/> fails with <see cref="JsonException"/>. Against
/// Redis a counter may still read as another numeric type; the fake is deliberately stricter.
/// </para>
/// <para>
/// <b>TEST-ONLY.</b> Never register it in a production container.
/// </para>
/// </remarks>
public sealed class FakeTypedHashStore<T> : ITypedHashStore<T>
{
    private readonly FakeHashStorage _storage;

    /// <summary>Creates a fake that uses <see cref="TimeProvider.System"/> for expiry.</summary>
    public FakeTypedHashStore()
        : this(TimeProvider.System)
    {
    }

    /// <summary>Creates a fake that uses <paramref name="timeProvider"/> for expiry.</summary>
    /// <param name="timeProvider">The time source.</param>
    public FakeTypedHashStore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _storage = new FakeHashStorage(timeProvider);
    }

    /// <summary>
    /// Gets or sets a value indicating whether every operation throws <see cref="TimeoutException"/> after
    /// argument validation, as if Redis did not answer.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>Gets every hash key that currently exists (not deleted, not expired).</summary>
    public IReadOnlyList<string> Keys => _storage.GetKeys();

    /// <inheritdoc />
    public ValueTask<CacheLookup<T>> GetFieldAsync(string key, string field, CancellationToken ct = default)
    {
        ValidateKeyAndField(key, field);
        BeforeOperation(ct);

        return ValueTask.FromResult(
            _storage.TryGetField(key, field, out var value) ? CacheLookup<T>.Hit(Cast(value, key, field)) : CacheLookup<T>.Miss);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, T>> GetFieldsAsync(string key, IEnumerable<string> fields, CancellationToken ct = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(fields);

        var names = fields.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var name in names)
            ValidateField(name);

        BeforeOperation(ct);

        var result = new Dictionary<string, T>(names.Length, StringComparer.Ordinal);
        foreach (var (field, value) in _storage.GetFields(key, names))
            result[field] = Cast(value, key, field);

        return ValueTask.FromResult<IReadOnlyDictionary<string, T>>(result);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, T>> GetAllFieldsAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        BeforeOperation(ct);

        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var (field, value) in _storage.GetAll(key))
            result[field] = Cast(value, key, field);

        return ValueTask.FromResult<IReadOnlyDictionary<string, T>>(result);
    }

    /// <inheritdoc />
    public ValueTask SetFieldAsync(string key, string field, T value, TimeSpan? timeToLive = null, CancellationToken ct = default)
    {
        ValidateKeyAndField(key, field);
        ValidateTimeToLive(timeToLive);
        BeforeOperation(ct);

        _storage.Set(key, [new KeyValuePair<string, object?>(field, value)], timeToLive);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask SetFieldsAsync(string key, IReadOnlyDictionary<string, T> values, TimeSpan? timeToLive = null, CancellationToken ct = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            throw new ArgumentException("At least one field is required.", nameof(values));

        var entries = new List<KeyValuePair<string, object?>>(values.Count);
        foreach (var (field, value) in values)
        {
            ValidateField(field);
            entries.Add(new KeyValuePair<string, object?>(field, value));
        }

        ValidateTimeToLive(timeToLive);
        BeforeOperation(ct);

        _storage.Set(key, entries, timeToLive);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<long> IncrementFieldAsync(string key, string field, long delta = 1, TimeSpan? timeToLive = null, CancellationToken ct = default)
    {
        ValidateKeyAndField(key, field);
        ValidateTimeToLive(timeToLive);
        BeforeOperation(ct);

        var updated = _storage.Increment(
            key,
            field,
            delta,
            timeToLive,
            value => value is long number
                ? number
                : throw new InvalidOperationException($"Field '{field}' of hash '{key}' does not hold an integer."),
            number => number);

        return ValueTask.FromResult(updated);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteFieldAsync(string key, string field, CancellationToken ct = default)
    {
        ValidateKeyAndField(key, field);
        BeforeOperation(ct);

        return ValueTask.FromResult(_storage.DeleteField(key, field));
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        BeforeOperation(ct);

        return ValueTask.FromResult(_storage.Delete(key));
    }

    /// <inheritdoc />
    public ValueTask<bool> ExpireAsync(string key, TimeSpan? timeToLive, CancellationToken ct = default)
    {
        ValidateKey(key);
        ValidateTimeToLive(timeToLive);
        BeforeOperation(ct);

        return ValueTask.FromResult(_storage.Expire(key, timeToLive));
    }

    /// <summary>Gets whether the hash exists (not deleted, not expired).</summary>
    /// <param name="key">The hash key.</param>
    /// <returns><see langword="true"/> when the hash has at least one field.</returns>
    public bool ContainsKey(string key) => _storage.ContainsKey(key);

    /// <summary>Gets how long until the hash expires.</summary>
    /// <param name="key">The hash key.</param>
    /// <returns>The remaining time, or <see langword="null"/> when the hash does not exist or has no expiry.</returns>
    public TimeSpan? GetTimeToLive(string key) => _storage.GetTimeToLive(key);

    /// <summary>Writes a field directly, bypassing <see cref="SimulateFailure"/>, for test setup.</summary>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field name.</param>
    /// <param name="value">The value.</param>
    public void Seed(string key, string field, T value)
    {
        ValidateKeyAndField(key, field);
        _storage.Set(key, [new KeyValuePair<string, object?>(field, value)], timeToLive: null);
    }

    /// <summary>Removes every hash.</summary>
    public void Reset() => _storage.Clear();

    private void BeforeOperation(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (SimulateFailure)
            throw new TimeoutException("Simulated Redis hash failure.");
    }

    private static T Cast(object? value, string key, string field) => value switch
    {
        T typed => typed,
        null => default!,
        _ => throw new JsonException($"Field '{field}' of hash '{key}' holds a {value.GetType().Name}, not a {typeof(T).Name}."),
    };
}
