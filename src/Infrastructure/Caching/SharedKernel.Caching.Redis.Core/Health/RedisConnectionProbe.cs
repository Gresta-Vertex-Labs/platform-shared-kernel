using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Health;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.Core.Health;

/// <summary>
/// The <see cref="IReadinessProbe"/> of the shared <see cref="IConnectionMultiplexer"/>, named
/// <see cref="RedisReadinessProbeNames.Connection"/>.
/// </summary>
/// <remarks>
/// It checks the connection every Redis package uses, so it reports what the cache, locks, hash store and
/// pub/sub actually see, without opening a connection of its own. An unreachable server is reported as
/// unhealthy, never thrown, and the description never contains the connection string or an exception message.
/// </remarks>
internal sealed class RedisConnectionProbe(IServiceProvider services) : IReadinessProbe
{
    public string Name => RedisReadinessProbeNames.Connection;

    public async Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Resolved here, not injected: building the multiplexer connects, and a host constructs every probe just to
        // read its name.
        var multiplexer = services.GetRequiredService<IConnectionMultiplexer>();
        if (!multiplexer.IsConnected)
            return ReadinessReport.Unhealthy("Not connected to Redis.");

        try
        {
            var latency = await multiplexer.GetDatabase().PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return ReadinessReport.Healthy(latency: latency);
        }
        // A dropped connection can also surface unwrapped: InvalidOperationException from a completed socket pipe or a
        // disposed multiplexer, IOException from the transport. Each is "Redis did not answer", not a probe bug.
        catch (Exception ex) when (ex is RedisException or TimeoutException or InvalidOperationException or IOException)
        {
            return ReadinessReport.Unhealthy(
                $"Redis did not answer PING ({ex.GetType().Name}).");
        }
    }
}
