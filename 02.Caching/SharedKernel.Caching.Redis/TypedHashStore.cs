using System.Text.Json.Serialization.Metadata;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.Redis;

/// <summary>
/// Internal AOT-safe implementation of <see cref="ITypedHashStore{T}"/>.
/// Captures <see cref="JsonTypeInfo{T}"/> at construction time and delegates all
/// operations to the underlying <see cref="IRedisHashService"/>.
/// </summary>
/// <typeparam name="T">The DTO type stored in the Redis hash.</typeparam>
internal sealed class TypedHashStore<T> : ITypedHashStore<T>
{
    private readonly IRedisHashService _hashService;
    private readonly JsonTypeInfo<T> _typeInfo;

    /// <summary>
    /// Initialises a new <see cref="TypedHashStore{T}"/>.
    /// </summary>
    /// <param name="hashService">The underlying Redis hash service.</param>
    /// <param name="typeInfo">The STJ type info for <typeparamref name="T"/>, captured once at startup.</param>
    public TypedHashStore(IRedisHashService hashService, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(hashService);
        ArgumentNullException.ThrowIfNull(typeInfo);
        _hashService = hashService;
        _typeInfo = typeInfo;
    }

    /// <inheritdoc />
    public ValueTask<T?> GetFieldAsync(string key, string field, CancellationToken ct = default) =>
        _hashService.GetFieldAsync(key, field, _typeInfo, ct);

    /// <inheritdoc />
    public ValueTask SetFieldAsync(string key, string field, T value, CancellationToken ct = default) =>
        _hashService.SetFieldAsync(key, field, value, _typeInfo, ct);

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, T>> GetAllFieldsAsync(string key, CancellationToken ct = default) =>
        _hashService.GetAllFieldsAsync(key, _typeInfo, ct);

    /// <inheritdoc />
    public ValueTask DeleteFieldAsync(string key, string field, CancellationToken ct = default) =>
        _hashService.DeleteFieldAsync(key, field, ct);

    /// <inheritdoc />
    public ValueTask<long> IncrementFieldAsync(string key, string field, long delta = 1, CancellationToken ct = default) =>
        _hashService.IncrementFieldAsync(key, field, delta, ct);
}
