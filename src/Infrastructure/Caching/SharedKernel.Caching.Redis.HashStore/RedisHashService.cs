using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Caching.Abstractions;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.HashStore;

/// <summary><see cref="IRedisHashService"/> over the shared <see cref="IConnectionMultiplexer"/>.</summary>
internal sealed class RedisHashService(IConnectionMultiplexer multiplexer) : IRedisHashService
{
    public async ValueTask<CacheLookup<T>> GetFieldAsync<T>(string key, string field, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        ValidateKeyAndField(key, field);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ct.ThrowIfCancellationRequested();

        var value = await Database.HashGetAsync(key, field).ConfigureAwait(false);
        return value.IsNull ? CacheLookup<T>.Miss : CacheLookup<T>.Hit(Deserialize(value, typeInfo));
    }

    public async ValueTask<IReadOnlyDictionary<string, T>> GetFieldsAsync<T>(
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

        ct.ThrowIfCancellationRequested();
        if (names.Length == 0)
            return new Dictionary<string, T>(0);

        var values = await Database.HashGetAsync(key, Array.ConvertAll(names, n => (RedisValue)n)).ConfigureAwait(false);

        var result = new Dictionary<string, T>(names.Length, StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
        {
            if (!values[i].IsNull)
                result[names[i]] = Deserialize(values[i], typeInfo);
        }

        return result;
    }

    public async ValueTask<IReadOnlyDictionary<string, T>> GetAllFieldsAsync<T>(string key, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ct.ThrowIfCancellationRequested();

        var entries = await Database.HashGetAllAsync(key).ConfigureAwait(false);

        var result = new Dictionary<string, T>(entries.Length, StringComparer.Ordinal);
        foreach (var entry in entries)
            result[entry.Name.ToString()] = Deserialize(entry.Value, typeInfo);

        return result;
    }

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

        return WriteAsync(key, [new HashEntry(field, Serialize(value, typeInfo))], timeToLive, ct);
    }

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

        var entries = new HashEntry[values.Count];
        var index = 0;
        foreach (var (field, value) in values)
        {
            ValidateField(field);
            entries[index++] = new HashEntry(field, Serialize(value, typeInfo));
        }

        return WriteAsync(key, entries, timeToLive, ct);
    }

    public async ValueTask<long> IncrementFieldAsync(
        string key,
        string field,
        long delta = 1,
        TimeSpan? timeToLive = null,
        CancellationToken ct = default)
    {
        ValidateKeyAndField(key, field);
        ValidateTimeToLive(timeToLive);
        ct.ThrowIfCancellationRequested();

        if (timeToLive is not { } ttl)
            return await Database.HashIncrementAsync(key, field, delta).ConfigureAwait(false);

        var result = await Database.ScriptEvaluateAsync(
            IncrementWithExpiryScript,
            [key],
            [field, delta, ToMilliseconds(ttl)]).ConfigureAwait(false);
        return (long)result;
    }

    public async ValueTask<bool> DeleteFieldAsync(string key, string field, CancellationToken ct = default)
    {
        ValidateKeyAndField(key, field);
        ct.ThrowIfCancellationRequested();

        return await Database.HashDeleteAsync(key, field).ConfigureAwait(false);
    }

    public async ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        ct.ThrowIfCancellationRequested();

        return await Database.KeyDeleteAsync(key).ConfigureAwait(false);
    }

    public async ValueTask<bool> ExpireAsync(string key, TimeSpan? timeToLive, CancellationToken ct = default)
    {
        ValidateKey(key);
        ValidateTimeToLive(timeToLive);
        ct.ThrowIfCancellationRequested();

        return timeToLive is { } ttl
            ? await Database.KeyExpireAsync(key, ttl).ConfigureAwait(false)
            : await Database.KeyPersistAsync(key).ConfigureAwait(false);
    }

    private IDatabase Database => multiplexer.GetDatabase();

    private async ValueTask WriteAsync(string key, HashEntry[] entries, TimeSpan? timeToLive, CancellationToken ct)
    {
        ValidateTimeToLive(timeToLive);
        ct.ThrowIfCancellationRequested();

        if (timeToLive is not { } ttl)
        {
            await Database.HashSetAsync(key, entries).ConfigureAwait(false);
            return;
        }

        var arguments = new RedisValue[(entries.Length * 2) + 1];
        arguments[0] = ToMilliseconds(ttl);
        for (var i = 0; i < entries.Length; i++)
        {
            arguments[(i * 2) + 1] = entries[i].Name;
            arguments[(i * 2) + 2] = entries[i].Value;
        }

        await Database.ScriptEvaluateAsync(SetWithExpiryScript, [key], arguments).ConfigureAwait(false);
    }

    // A script, not MULTI/EXEC: Redis does not roll back a transaction, so a failed HSET (for example on a key of
    // another type) would still apply the expiry. A failing redis.call stops the script before PEXPIRE runs.
    private const string SetWithExpiryScript = """
        for i = 2, #ARGV, 2 do
          redis.call('HSET', KEYS[1], ARGV[i], ARGV[i + 1])
        end
        redis.call('PEXPIRE', KEYS[1], ARGV[1])
        return 1
        """;

    private const string IncrementWithExpiryScript = """
        local value = redis.call('HINCRBY', KEYS[1], ARGV[1], ARGV[2])
        redis.call('PEXPIRE', KEYS[1], ARGV[3])
        return value
        """;

    // PEXPIRE takes whole milliseconds and rejects 0.
    private static long ToMilliseconds(TimeSpan timeToLive) => Math.Max(1, (long)Math.Ceiling(timeToLive.TotalMilliseconds));

    private static string Serialize<T>(T value, JsonTypeInfo<T> typeInfo) => JsonSerializer.Serialize(value, typeInfo);

    private static T Deserialize<T>(RedisValue value, JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize((string)value!, typeInfo)!;

    private static void ValidateKeyAndField(string key, string field)
    {
        ValidateKey(key);
        ValidateField(field);
    }

    private static void ValidateKey(string key) => ArgumentException.ThrowIfNullOrWhiteSpace(key);

    private static void ValidateField(string field) => ArgumentException.ThrowIfNullOrEmpty(field);

    private static void ValidateTimeToLive(TimeSpan? timeToLive)
    {
        if (timeToLive is { } ttl)
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero, nameof(timeToLive));
    }
}
