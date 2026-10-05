using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.Redis.HashStore;

/// <summary>
/// Reads and writes individual fields of Redis hashes, with values stored as JSON.
/// </summary>
/// <remarks>
/// <para>
/// A hash holds named fields under one key, so a session, a settings snapshot or a set of counters can be
/// read or changed one field at a time instead of rewriting a whole object. Keys are used exactly as given:
/// include the service and any tenant in the key yourself.
/// </para>
/// <para>
/// Values are serialized with the <see cref="JsonTypeInfo{T}"/> passed to each call, typically from a
/// source-generated <c>JsonSerializerContext</c>. A stored value that no longer matches the type fails with
/// <see cref="JsonException"/>. Use <see cref="ITypedHashStore{T}"/> to fix the type once at registration.
/// </para>
/// <para>
/// A <c>timeToLive</c> on a write sets the expiry of the whole key in the same atomic step, so a session
/// written with one never outlives it; each such write restarts the expiry. Redis failures surface as
/// <c>StackExchange.Redis.RedisException</c> or <see cref="TimeoutException"/>. Cancellation is checked
/// before the command is sent; a command already sent is not interrupted.
/// </para>
/// </remarks>
public interface IRedisHashService
{
    /// <summary>Reads one field.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field name.</param>
    /// <param name="typeInfo">The JSON contract for <typeparamref name="T"/>.</param>
    /// <param name="ct">A token to cancel the call.</param>
    /// <returns>A hit with the value, or a miss when the key or field does not exist.</returns>
    ValueTask<CacheLookup<T>> GetFieldAsync<T>(string key, string field, JsonTypeInfo<T> typeInfo, CancellationToken ct = default);

    /// <summary>Reads several fields in one round trip.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The hash key.</param>
    /// <param name="fields">The field names; duplicates are read once.</param>
    /// <param name="typeInfo">The JSON contract for <typeparamref name="T"/>.</param>
    /// <param name="ct">A token to cancel the call.</param>
    /// <returns>The fields that exist, by name; missing fields are absent.</returns>
    ValueTask<IReadOnlyDictionary<string, T>> GetFieldsAsync<T>(
        string key,
        IEnumerable<string> fields,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct = default);

    /// <summary>Reads every field of a hash.</summary>
    /// <typeparam name="T">The value type of every field.</typeparam>
    /// <param name="key">The hash key.</param>
    /// <param name="typeInfo">The JSON contract for <typeparamref name="T"/>.</param>
    /// <param name="ct">A token to cancel the call.</param>
    /// <returns>Every field by name; empty when the key does not exist.</returns>
    /// <remarks>Reads the whole hash in one command; keep hashes read this way small.</remarks>
    ValueTask<IReadOnlyDictionary<string, T>> GetAllFieldsAsync<T>(string key, JsonTypeInfo<T> typeInfo, CancellationToken ct = default);

    /// <summary>Writes one field, creating the hash when needed.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field name.</param>
    /// <param name="value">The value.</param>
    /// <param name="typeInfo">The JSON contract for <typeparamref name="T"/>.</param>
    /// <param name="timeToLive">When set, the key expires this long after the write; must be positive.</param>
    /// <param name="ct">A token to cancel the call.</param>
    /// <returns>A task that completes when the write is acknowledged.</returns>
    ValueTask SetFieldAsync<T>(
        string key,
        string field,
        T value,
        JsonTypeInfo<T> typeInfo,
        TimeSpan? timeToLive = null,
        CancellationToken ct = default);

    /// <summary>Writes several fields in one atomic step, creating the hash when needed.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The hash key.</param>
    /// <param name="values">The fields to write, by name; must not be empty.</param>
    /// <param name="typeInfo">The JSON contract for <typeparamref name="T"/>.</param>
    /// <param name="timeToLive">When set, the key expires this long after the write; must be positive.</param>
    /// <param name="ct">A token to cancel the call.</param>
    /// <returns>A task that completes when the write is acknowledged.</returns>
    ValueTask SetFieldsAsync<T>(
        string key,
        IReadOnlyDictionary<string, T> values,
        JsonTypeInfo<T> typeInfo,
        TimeSpan? timeToLive = null,
        CancellationToken ct = default);

    /// <summary>Adds <paramref name="delta"/> to an integer field atomically, creating it at zero when missing.</summary>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field name.</param>
    /// <param name="delta">The amount to add; negative to subtract.</param>
    /// <param name="timeToLive">When set, the key expires this long after the change; must be positive.</param>
    /// <param name="ct">A token to cancel the call.</param>
    /// <returns>The field's new value.</returns>
    /// <remarks>The field must hold an integer; any other value fails with a Redis server error.</remarks>
    ValueTask<long> IncrementFieldAsync(
        string key,
        string field,
        long delta = 1,
        TimeSpan? timeToLive = null,
        CancellationToken ct = default);

    /// <summary>Removes one field.</summary>
    /// <param name="key">The hash key.</param>
    /// <param name="field">The field name.</param>
    /// <param name="ct">A token to cancel the call.</param>
    /// <returns><see langword="true"/> when the field existed.</returns>
    ValueTask<bool> DeleteFieldAsync(string key, string field, CancellationToken ct = default);

    /// <summary>Removes the whole hash.</summary>
    /// <param name="key">The hash key.</param>
    /// <param name="ct">A token to cancel the call.</param>
    /// <returns><see langword="true"/> when the key existed.</returns>
    ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Sets or removes the expiry of the whole hash.</summary>
    /// <param name="key">The hash key.</param>
    /// <param name="timeToLive">How long until the key expires; must be positive. <see langword="null"/> removes the expiry.</param>
    /// <param name="ct">A token to cancel the call.</param>
    /// <returns><see langword="true"/> when the key exists and its expiry changed.</returns>
    ValueTask<bool> ExpireAsync(string key, TimeSpan? timeToLive, CancellationToken ct = default);
}
