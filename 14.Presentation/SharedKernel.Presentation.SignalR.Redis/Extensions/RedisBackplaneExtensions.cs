using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Presentation.SignalR.Extensions;

/// <summary>
/// DI extension for opting a SignalR server into the Redis-backed scale-out backplane. Its own package since
/// P-570, so a single-replica SignalR host does not reference StackExchange.Redis; declared in the
/// <c>SharedKernel.Presentation.SignalR.Extensions</c> namespace so <c>AddSharedKernelSignalR().WithRedisBackplane(...)</c>
/// needs no extra <c>using</c>.
/// </summary>
public static class RedisBackplaneExtensions
{
    /// <summary>
    /// Wires the Redis-backed SignalR scale-out backplane.
    /// </summary>
    /// <param name="builder">The SignalR server builder to extend.</param>
    /// <param name="connectionString">The Redis connection string.</param>
    /// <param name="configure">An optional callback to further configure <see cref="RedisOptions"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for chaining.</returns>
    /// <remarks>
    /// Thin pass-through wrapper over
    /// <c>builder.AddStackExchangeRedis(connectionString, configure)</c> — exists so consuming
    /// services have one discoverable, platform-named extension method instead of reaching for
    /// the underlying NuGet package's extension directly. No added behavior beyond pass-through.
    /// This is purely additive/opt-in — omitting it keeps SignalR fully in-memory (single-instance
    /// only), which is correct for local dev and single-replica deployments.
    /// <para>
    /// Never shares an <see cref="StackExchange.Redis.IConnectionMultiplexer"/> instance with
    /// <c>02.Caching.Redis.Core</c>'s connection management — <c>AddStackExchangeRedis</c> manages
    /// its own connection lifecycle internally, and the two domains must not be wired together.
    /// This is intentional isolation, not an oversight: a backplane outage must not be conflated
    /// with a cache-connection outage in health checks or logs.
    /// </para>
    /// </remarks>
    public static ISignalRServerBuilder WithRedisBackplane(
        this ISignalRServerBuilder builder,
        string connectionString,
        Action<RedisOptions>? configure = null)
        => configure is null
            ? builder.AddStackExchangeRedis(connectionString)
            : builder.AddStackExchangeRedis(connectionString, configure);
}
