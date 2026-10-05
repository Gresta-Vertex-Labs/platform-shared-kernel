using System.Text.Json.Serialization.Metadata;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.Redis.HashStore;

/// <summary><see cref="ITypedHashStore{T}"/> that passes a fixed <see cref="JsonTypeInfo{T}"/> to <see cref="IRedisHashService"/>.</summary>
internal sealed class TypedHashStore<T>(IRedisHashService hashService, JsonTypeInfo<T> typeInfo) : ITypedHashStore<T>
{
    public ValueTask<CacheLookup<T>> GetFieldAsync(string key, string field, CancellationToken ct = default) =>
        hashService.GetFieldAsync(key, field, typeInfo, ct);

    public ValueTask<IReadOnlyDictionary<string, T>> GetFieldsAsync(string key, IEnumerable<string> fields, CancellationToken ct = default) =>
        hashService.GetFieldsAsync(key, fields, typeInfo, ct);

    public ValueTask<IReadOnlyDictionary<string, T>> GetAllFieldsAsync(string key, CancellationToken ct = default) =>
        hashService.GetAllFieldsAsync(key, typeInfo, ct);

    public ValueTask SetFieldAsync(string key, string field, T value, TimeSpan? timeToLive = null, CancellationToken ct = default) =>
        hashService.SetFieldAsync(key, field, value, typeInfo, timeToLive, ct);

    public ValueTask SetFieldsAsync(string key, IReadOnlyDictionary<string, T> values, TimeSpan? timeToLive = null, CancellationToken ct = default) =>
        hashService.SetFieldsAsync(key, values, typeInfo, timeToLive, ct);

    public ValueTask<long> IncrementFieldAsync(string key, string field, long delta = 1, TimeSpan? timeToLive = null, CancellationToken ct = default) =>
        hashService.IncrementFieldAsync(key, field, delta, timeToLive, ct);

    public ValueTask<bool> DeleteFieldAsync(string key, string field, CancellationToken ct = default) =>
        hashService.DeleteFieldAsync(key, field, ct);

    public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default) =>
        hashService.DeleteAsync(key, ct);

    public ValueTask<bool> ExpireAsync(string key, TimeSpan? timeToLive, CancellationToken ct = default) =>
        hashService.ExpireAsync(key, timeToLive, ct);
}
