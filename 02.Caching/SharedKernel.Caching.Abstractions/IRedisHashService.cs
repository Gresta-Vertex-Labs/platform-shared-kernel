using System.Text.Json.Serialization.Metadata;

namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Provides structured field-value storage using Redis Hash data structures.
/// </summary>
/// <remarks>
/// <para>
/// Redis Hashes are appropriate when you need to store and retrieve individual fields of a
/// structured object without serializing or deserializing the entire object. Common use cases
/// include user sessions, configuration snapshots, and aggregated counters.
/// </para>
/// <para>
/// All typed methods accept <see cref="JsonTypeInfo{T}"/> to ensure AOT-safe, reflection-free
/// serialization. Obtain the type info from a source-generated <c>JsonSerializerContext</c>.
/// </para>
/// <para>
/// The underlying Redis connection is shared with other Redis services registered in the same
/// DI container — no additional connections are created.
/// </para>
/// </remarks>
public interface IRedisHashService
{
    /// <summary>
    /// Gets the value of a single <paramref name="field"/> within the Redis hash at <paramref name="key"/>.
    /// Returns <see langword="null"/> if the key or field does not exist.
    /// </summary>
    /// <typeparam name="T">The deserialized field value type.</typeparam>
    /// <param name="key">The Redis hash key.</param>
    /// <param name="field">The field name within the hash.</param>
    /// <param name="typeInfo">
    /// Source-generated <see cref="JsonTypeInfo{T}"/> used for AOT-safe deserialization.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deserialized field value, or <see langword="null"/> if not found.</returns>
    ValueTask<T?> GetFieldAsync<T>(string key, string field, JsonTypeInfo<T> typeInfo, CancellationToken ct = default);

    /// <summary>
    /// Sets the value of a single <paramref name="field"/> within the Redis hash at <paramref name="key"/>.
    /// Creates the hash if it does not exist.
    /// </summary>
    /// <typeparam name="T">The field value type to serialize.</typeparam>
    /// <param name="key">The Redis hash key.</param>
    /// <param name="field">The field name within the hash.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="typeInfo">
    /// Source-generated <see cref="JsonTypeInfo{T}"/> used for AOT-safe serialization.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask SetFieldAsync<T>(string key, string field, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct = default);

    /// <summary>
    /// Gets all fields and their deserialized values from the Redis hash at <paramref name="key"/>.
    /// Returns an empty dictionary if the key does not exist.
    /// </summary>
    /// <typeparam name="T">The field value type to deserialize.</typeparam>
    /// <param name="key">The Redis hash key.</param>
    /// <param name="typeInfo">
    /// Source-generated <see cref="JsonTypeInfo{T}"/> used for AOT-safe deserialization.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A read-only dictionary mapping field names to their deserialized values.
    /// Fields whose values cannot be deserialized are omitted.
    /// </returns>
    ValueTask<IReadOnlyDictionary<string, T>> GetAllFieldsAsync<T>(string key, JsonTypeInfo<T> typeInfo, CancellationToken ct = default);

    /// <summary>
    /// Deletes the specified <paramref name="field"/> from the Redis hash at <paramref name="key"/>.
    /// No-ops if the key or field does not exist.
    /// </summary>
    /// <param name="key">The Redis hash key.</param>
    /// <param name="field">The field name to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask DeleteFieldAsync(string key, string field, CancellationToken ct = default);

    /// <summary>
    /// Atomically increments the integer value of <paramref name="field"/> in the Redis hash at
    /// <paramref name="key"/> by <paramref name="delta"/>. Creates the field with value
    /// <paramref name="delta"/> if it does not exist.
    /// </summary>
    /// <param name="key">The Redis hash key.</param>
    /// <param name="field">The field name to increment.</param>
    /// <param name="delta">The amount to add. Use a negative value to decrement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new value of the field after the increment.</returns>
    ValueTask<long> IncrementFieldAsync(string key, string field, long delta = 1, CancellationToken ct = default);
}
