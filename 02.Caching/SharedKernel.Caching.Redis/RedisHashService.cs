using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Caching.Abstractions;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis;

/// <summary>
/// <see cref="IRedisHashService"/> implementation backed by StackExchange.Redis hash commands.
/// </summary>
/// <remarks>
/// The <see cref="IDatabase"/> reference is obtained once from the shared
/// <see cref="IConnectionMultiplexer"/> singleton and reused for all operations —
/// no new connections are created.
/// All typed methods use <see cref="JsonTypeInfo{T}"/> for AOT-safe, reflection-free serialization.
/// </remarks>
internal sealed class RedisHashService : IRedisHashService
{
    // IDatabase is a lightweight view on the shared connection — caching is correct and expected.
    private readonly IDatabase _db;

    /// <summary>
    /// Initialises a new <see cref="RedisHashService"/> using the shared multiplexer.
    /// </summary>
    /// <param name="multiplexer">The singleton Redis connection multiplexer.</param>
    public RedisHashService(IConnectionMultiplexer multiplexer)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        _db = multiplexer.GetDatabase();
    }

    /// <inheritdoc />
    public async ValueTask<T?> GetFieldAsync<T>(
        string key,
        string field,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(typeInfo);

        var value = await _db.HashGetAsync(key, field).ConfigureAwait(false);

        if (!value.HasValue || value.IsNull)
            return default;

        var json = (string?)value;
        if (json is null)
            return default;

        return JsonSerializer.Deserialize(json, typeInfo);
    }

    /// <inheritdoc />
    public async ValueTask SetFieldAsync<T>(
        string key,
        string field,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(typeInfo);

        var json = JsonSerializer.Serialize(value, typeInfo);
        await _db.HashSetAsync(key, field, json).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, T>> GetAllFieldsAsync<T>(
        string key,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(typeInfo);

        var entries = await _db.HashGetAllAsync(key).ConfigureAwait(false);

        if (entries.Length == 0)
            return new Dictionary<string, T>(0);

        var result = new Dictionary<string, T>(entries.Length, StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var fieldName = (string?)entry.Name;
            var rawValue = (string?)entry.Value;

            if (fieldName is null || rawValue is null)
                continue;

            var deserialized = JsonSerializer.Deserialize(rawValue, typeInfo);
            if (deserialized is not null)
                result[fieldName] = deserialized;
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask DeleteFieldAsync(string key, string field, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        await _db.HashDeleteAsync(key, field).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<long> IncrementFieldAsync(
        string key,
        string field,
        long delta = 1,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        return await _db.HashIncrementAsync(key, field, delta).ConfigureAwait(false);
    }
}
