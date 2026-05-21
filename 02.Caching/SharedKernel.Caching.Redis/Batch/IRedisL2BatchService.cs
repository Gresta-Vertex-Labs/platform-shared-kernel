using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.Batch;

// Internal helper that batches Redis GET operations into a single pipeline round-trip.
// Not registered in DI as a public service — used internally to optimise L2 reads.
// Callers are responsible for key serialization (the v2: prefix is applied by the
// Microsoft.Extensions.Caching.StackExchangeRedis layer; raw StackExchange.Redis keys
// must include that prefix to match stored entries).
internal interface IRedisL2BatchService
{
    /// <summary>
    /// Retrieves the raw byte payloads for multiple keys in a single Redis pipeline round-trip.
    /// Keys that do not exist in Redis map to <see langword="null"/> in the returned dictionary.
    /// An empty <paramref name="redisKeys"/> array returns an empty dictionary immediately.
    /// </summary>
    /// <param name="redisKeys">
    /// The raw Redis keys (including any prefix applied by the storage layer).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A dictionary mapping each raw Redis key to its stored byte payload, or
    /// <see langword="null"/> when the key is absent.
    /// </returns>
    ValueTask<IReadOnlyDictionary<RedisKey, byte[]?>> GetManyRawAsync(
        IReadOnlyCollection<RedisKey> redisKeys,
        CancellationToken ct = default);
}
