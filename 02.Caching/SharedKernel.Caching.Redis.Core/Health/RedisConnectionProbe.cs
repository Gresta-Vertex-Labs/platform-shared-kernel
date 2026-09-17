using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.Core.Health;

/// <summary><see cref="IRedisConnectionProbe"/> over the shared <see cref="IConnectionMultiplexer"/>.</summary>
internal sealed class RedisConnectionProbe(IConnectionMultiplexer multiplexer) : IRedisConnectionProbe
{
    public async Task<RedisConnectionHealth> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!multiplexer.IsConnected)
            return new RedisConnectionHealth(false, null, "Not connected to Redis.");

        try
        {
            var latency = await multiplexer.GetDatabase().PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return new RedisConnectionHealth(true, latency, null);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return new RedisConnectionHealth(false, null, $"Redis did not answer PING ({ex.GetType().Name}).");
        }
    }
}
