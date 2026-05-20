namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// AOT-safe typed wrapper over <see cref="IRedisHashService"/> for a specific DTO type
/// <typeparamref name="T"/>. Eliminates per-call <c>JsonTypeInfo&lt;T&gt;</c> passing by
/// capturing the type info once at DI registration time.
/// </summary>
/// <typeparam name="T">The DTO type stored in the Redis hash.</typeparam>
/// <remarks>
/// <para>
/// <strong>AOT registration contract:</strong> Register one <c>ITypedHashStore&lt;T&gt;</c>
/// per DTO type at startup using the <c>AddTypedHashStore&lt;T&gt;(JsonTypeInfo&lt;T&gt;)</c>
/// extension method on <c>ICachingBuilder</c>, then inject <c>ITypedHashStore&lt;T&gt;</c>
/// directly into your service — no per-call type info is required.
/// </para>
/// <para>
/// Example:
/// <code>
/// // Startup registration
/// services.AddSharedKernelCaching(o => o.SerializerContext = MyAppSerializerContext.Default)
///         .AddRedisL2(connectionString)
///         .AddRedisHashService()
///         .AddTypedHashStore(MyAppSerializerContext.Default.OrderDto)
///         .AddTypedHashStore(MyAppSerializerContext.Default.CustomerDto);
///
/// // Injection
/// public class OrderCacheService(ITypedHashStore&lt;OrderDto&gt; store) { ... }
/// </code>
/// </para>
/// <para>
/// <see cref="IRedisHashService"/> with explicit <c>JsonTypeInfo&lt;T&gt;</c> parameters
/// remains available as the low-level primitive for generic infrastructure code.
/// Neither API is deprecated.
/// </para>
/// </remarks>
public interface ITypedHashStore<T>
{
    /// <summary>
    /// Gets the value of a single hash field, deserialising it to <typeparamref name="T"/>.
    /// Returns <see langword="null"/> when the key or field does not exist.
    /// </summary>
    /// <param name="key">The Redis key of the hash.</param>
    /// <param name="field">The field name within the hash.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deserialised value, or <see langword="null"/> if absent.</returns>
    ValueTask<T?> GetFieldAsync(string key, string field, CancellationToken ct = default);

    /// <summary>
    /// Serialises <paramref name="value"/> and stores it in the specified hash field.
    /// Creates the key and field if they do not exist.
    /// </summary>
    /// <param name="key">The Redis key of the hash.</param>
    /// <param name="field">The field name within the hash.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask SetFieldAsync(string key, string field, T value, CancellationToken ct = default);

    /// <summary>
    /// Returns all fields and their deserialised values for the given hash key.
    /// Returns an empty dictionary when the key does not exist.
    /// </summary>
    /// <param name="key">The Redis key of the hash.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only dictionary mapping field names to deserialised values.</returns>
    ValueTask<IReadOnlyDictionary<string, T>> GetAllFieldsAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Deletes a single field from the hash. No-op when the key or field does not exist.
    /// </summary>
    /// <param name="key">The Redis key of the hash.</param>
    /// <param name="field">The field name to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask DeleteFieldAsync(string key, string field, CancellationToken ct = default);

    /// <summary>
    /// Atomically increments the integer value stored in the specified hash field by
    /// <paramref name="delta"/>. Creates the field with value <paramref name="delta"/>
    /// when it does not yet exist.
    /// </summary>
    /// <param name="key">The Redis key of the hash.</param>
    /// <param name="field">The field name to increment.</param>
    /// <param name="delta">The amount to add (use negative values to decrement).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new value of the field after the increment.</returns>
    ValueTask<long> IncrementFieldAsync(string key, string field, long delta = 1, CancellationToken ct = default);
}
