using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.HashStore;
using static SharedKernel.Testing.Caching.FakeHashStorage;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="IRedisHashService"/> for use in unit tests. Thread-safe.
/// </summary>
/// <remarks>
/// <para>
/// Field values are stored as the JSON the real service would write, using the <see cref="JsonTypeInfo{T}"/>
/// passed to each call, and read back through it. A value that no longer matches the requested type fails
/// with <see cref="JsonException"/>, as it does against Redis.
/// </para>
/// <para>
/// A <c>timeToLive</c> sets the expiry of the whole key together with the write, and each such write restarts
/// it. Expiry is measured with the <see cref="TimeProvider"/> passed to the constructor, so a test can advance
/// a fake time provider to expire a hash. <see cref="IncrementFieldAsync"/> follows <c>HINCRBY</c>: a missing
/// field starts at zero, and a field that does not hold an integer fails with
/// <see cref="InvalidOperationException"/>, standing in for the Redis server error.
/// </para>
/// <para>
/// Argument validation matches the real service: a null or whitespace key and a null or empty field throw
/// <see cref="ArgumentException"/>, a non-positive time-to-live throws
/// <see cref="ArgumentOutOfRangeException"/>, and writing an empty set of fields throws
/// <see cref="ArgumentException"/>. Cancellation is checked before the operation.
/// </para>
/// <para>
/// <b>TEST-ONLY.</b> Never register it in a production container.
/// </para>
/// </remarks>
public sealed class FakeRedisHashService : IRedisHashService
{
    private readonly FakeHashStorage _storage;

    /// <summary>Creates a fake that uses <see cref="TimeProvider.System"/> for expiry.</summary>
    public FakeRedisHashService()
        : this(TimeProvider.System)
    {
    }

    /// <summary>Creates a fake that uses <paramref name="timeProvider"/> for expiry.</summary>
    /// <param name="timeProvider">The time source.</param>
    public FakeRedisHashService(TimeProvider timeProvider)
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
    public ValueTask<CacheLookup<T>> GetFieldAsync<T>(string key, string field, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        ValidateKeyAndField(key, field);
        ArgumentNullException.ThrowIfNull(typeInfo);
        BeforeOperation(ct);

        return ValueTask.FromResult(
            _storage.TryGetField(key, field, out var raw)
                ? CacheLookup<T>.Hit(Deserialize(raw, typeInfo))
                : CacheLookup<T>.Miss);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, T>> GetFieldsAsync<T>(
        string key,
        IEnumerable<string> fields,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(typeInfo);

        var names = fields.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var name in names)
            ValidateField(name);

        BeforeOperation(ct);

        var result = new Dictionary<string, T>(names.Length, StringComparer.Ordinal);
        foreach (var (field, raw) in _storage.GetFields(key, names))
            result[field] = Deserialize(raw, typeInfo);

        return ValueTask.FromResult<IReadOnlyDictionary<string, T>>(result);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, T>> GetAllFieldsAsync<T>(string key, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(typeInfo);
        BeforeOperation(ct);

        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var (field, raw) in _storage.GetAll(key))
            result[field] = Deserialize(raw, typeInfo);

        return ValueTask.FromResult<IReadOnlyDictionary<string, T>>(result);
    }

    /// <inheritdoc />
    public ValueTask SetFieldAsync<T>(
        string key,
        string field,
        T value,
        JsonTypeInfo<T> typeInfo,
        TimeSpan? timeToLive = null,
        CancellationToken ct = default)
    {
        ValidateKeyAndField(key, field);
        ArgumentNullException.ThrowIfNull(typeInfo);
        var json = JsonSerializer.Serialize(value, typeInfo);
        ValidateTimeToLive(timeToLive);
        BeforeOperation(ct);

        _storage.Set(key, [new KeyValuePair<string, object?>(field, json)], timeToLive);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask SetFieldsAsync<T>(
        string key,
        IReadOnlyDictionary<string, T> values,
        JsonTypeInfo<T> typeInfo,
        TimeSpan? timeToLive = null,
        CancellationToken ct = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (values.Count == 0)
            throw new ArgumentException("At least one field is required.", nameof(values));

        var entries = new List<KeyValuePair<string, object?>>(values.Count);
        foreach (var (field, value) in values)
        {
            ValidateField(field);
            entries.Add(new KeyValuePair<string, object?>(field, JsonSerializer.Serialize(value, typeInfo)));
        }

        ValidateTimeToLive(timeToLive);
        BeforeOperation(ct);

        _storage.Set(key, entries, timeToLive);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<long> IncrementFieldAsync(
        string key,
        string field,
        long delta = 1,
        TimeSpan? timeToLive = null,
        CancellationToken ct = default)
    {
        ValidateKeyAndField(key, field);
        ValidateTimeToLive(timeToLive);
        BeforeOperation(ct);

        var updated = _storage.Increment(
            key,
            field,
            delta,
            timeToLive,
            raw => raw is string text
                   && !text.StartsWith('+')
                   && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                ? number
                : throw new InvalidOperationException($"Field '{field}' of hash '{key}' does not hold an integer."),
            number => number.ToString(CultureInfo.InvariantCulture));

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

    /// <summary>Gets the raw stored text of a field, as Redis would hold it.</summary>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field name.</param>
    /// <returns>The stored JSON or counter text, or <see langword="null"/> when the field does not exist.</returns>
    public string? GetRawField(string key, string field) =>
        _storage.TryGetField(key, field, out var raw) ? (string?)raw : null;

    /// <summary>Writes a field directly, bypassing <see cref="SimulateFailure"/>, for test setup.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field name.</param>
    /// <param name="value">The value.</param>
    /// <param name="typeInfo">The JSON contract used to store it.</param>
    public void Seed<T>(string key, string field, T value, JsonTypeInfo<T> typeInfo)
    {
        ValidateKeyAndField(key, field);
        ArgumentNullException.ThrowIfNull(typeInfo);

        _storage.Set(key, [new KeyValuePair<string, object?>(field, JsonSerializer.Serialize(value, typeInfo))], timeToLive: null);
    }

    /// <summary>
    /// Writes raw text into a field directly, for test setup such as a value in an outdated or malformed format.
    /// </summary>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field name.</param>
    /// <param name="rawValue">The text to store as-is.</param>
    public void SeedRaw(string key, string field, string rawValue)
    {
        ValidateKeyAndField(key, field);
        ArgumentNullException.ThrowIfNull(rawValue);

        _storage.Set(key, [new KeyValuePair<string, object?>(field, rawValue)], timeToLive: null);
    }

    /// <summary>Removes every hash.</summary>
    public void Reset() => _storage.Clear();

    private void BeforeOperation(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (SimulateFailure)
            throw new TimeoutException("Simulated Redis hash failure.");
    }

    private static T Deserialize<T>(object? raw, JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize((string)raw!, typeInfo)!;
}
