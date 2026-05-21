using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.Batch;

// Pipelines multiple StringGetAsync calls into a single Redis round-trip using
// IDatabase.CreateBatch(). This avoids N separate network requests for GetManyAsync.
// Not registered in DI — instantiated directly by tests that need pipeline verification.
internal sealed class RedisL2BatchService : IRedisL2BatchService
{
    private readonly IDatabase _database;

    internal RedisL2BatchService(IConnectionMultiplexer multiplexer)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        _database = multiplexer.GetDatabase();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<RedisKey, byte[]?>> GetManyRawAsync(
        IReadOnlyCollection<RedisKey> redisKeys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(redisKeys);

        if (redisKeys.Count == 0)
            return new Dictionary<RedisKey, byte[]?>();

        // Create a batch so all GET commands are sent to Redis in a single pipeline flush.
        var batch = _database.CreateBatch();

        // Enqueue all GET tasks before executing — this is the pipeline pattern.
        var pendingTasks = new (RedisKey Key, Task<RedisValue> Task)[redisKeys.Count];
        var index = 0;

        foreach (var key in redisKeys)
            pendingTasks[index++] = (key, batch.StringGetAsync(key));

        // Execute flushes the pipeline: all enqueued commands are sent as a single write.
        batch.Execute();

        var result = new Dictionary<RedisKey, byte[]?>(redisKeys.Count);

        foreach (var (key, task) in pendingTasks)
        {
            ct.ThrowIfCancellationRequested();
            var value = await task.ConfigureAwait(false);
            result[key] = value.IsNull ? null : (byte[])value!;
        }

        return result;
    }
}
