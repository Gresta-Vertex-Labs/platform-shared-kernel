using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.Redis.HashStore;

/// <summary>
/// <see cref="IRedisHashService"/> for one value type, whose JSON contract is fixed at registration.
/// </summary>
/// <typeparam name="T">The type of every field value.</typeparam>
/// <remarks>
/// Register one per type with <c>AddTypedHashStore(MyJsonContext.Default.SessionDto)</c> and inject
/// <c>ITypedHashStore&lt;SessionDto&gt;</c>. Every member behaves as the <see cref="IRedisHashService"/> member
/// of the same name.
/// </remarks>
/// <example>
/// <code>
/// public sealed class SessionStore(ITypedHashStore&lt;SessionDto&gt; sessions)
/// {
///     public ValueTask SaveAsync(string sessionId, SessionDto session, CancellationToken ct) =>
///         sessions.SetFieldAsync($"auth:session:{sessionId}", "data", session, TimeSpan.FromMinutes(30), ct);
/// }
/// </code>
/// </example>
public interface ITypedHashStore<T>
{
    /// <inheritdoc cref="IRedisHashService.GetFieldAsync{T}"/>
    ValueTask<CacheLookup<T>> GetFieldAsync(string key, string field, CancellationToken ct = default);

    /// <inheritdoc cref="IRedisHashService.GetFieldsAsync{T}"/>
    ValueTask<IReadOnlyDictionary<string, T>> GetFieldsAsync(string key, IEnumerable<string> fields, CancellationToken ct = default);

    /// <inheritdoc cref="IRedisHashService.GetAllFieldsAsync{T}"/>
    ValueTask<IReadOnlyDictionary<string, T>> GetAllFieldsAsync(string key, CancellationToken ct = default);

    /// <inheritdoc cref="IRedisHashService.SetFieldAsync{T}"/>
    ValueTask SetFieldAsync(string key, string field, T value, TimeSpan? timeToLive = null, CancellationToken ct = default);

    /// <inheritdoc cref="IRedisHashService.SetFieldsAsync{T}"/>
    ValueTask SetFieldsAsync(string key, IReadOnlyDictionary<string, T> values, TimeSpan? timeToLive = null, CancellationToken ct = default);

    /// <inheritdoc cref="IRedisHashService.IncrementFieldAsync"/>
    ValueTask<long> IncrementFieldAsync(string key, string field, long delta = 1, TimeSpan? timeToLive = null, CancellationToken ct = default);

    /// <inheritdoc cref="IRedisHashService.DeleteFieldAsync"/>
    ValueTask<bool> DeleteFieldAsync(string key, string field, CancellationToken ct = default);

    /// <inheritdoc cref="IRedisHashService.DeleteAsync"/>
    ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default);

    /// <inheritdoc cref="IRedisHashService.ExpireAsync"/>
    ValueTask<bool> ExpireAsync(string key, TimeSpan? timeToLive, CancellationToken ct = default);
}
