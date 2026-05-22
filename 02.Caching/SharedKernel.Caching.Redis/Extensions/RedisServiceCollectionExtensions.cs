using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Polly.CircuitBreaker;
using SharedKernel.Caching.Abstractions;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;

namespace SharedKernel.Caching.Redis.Extensions;

/// <summary>
/// Minimal <see cref="ICachingBuilder"/> implementation used internally in the Redis package
/// to allow the <c>IServiceCollection</c> shim overload of <c>AddRedisDistributedLocking</c>
/// to delegate to the canonical <c>ICachingBuilder</c> extension without taking a dependency
/// on <c>SharedKernel.Caching.FusionCache</c> (sibling layering rule).
/// </summary>
internal sealed class RedisCachingBuilder(IServiceCollection services) : ICachingBuilder
{
    public IServiceCollection Services { get; } = services;
}

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering Redis-backed caching
/// infrastructure: L2 distributed backplane and distributed locking via RedLock.net.
/// </summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Redis L2 distributed backplane to the FusionCache instance registered by
    /// <c>AddSharedKernelCaching</c>. Must be called <b>after</b> <c>AddSharedKernelCaching</c>.
    /// </summary>
    /// <param name="builder">
    /// The caching builder returned by <c>AddSharedKernelCaching</c>.
    /// </param>
    /// <param name="connectionString">
    /// A StackExchange.Redis connection string (e.g., <c>"localhost:6379"</c>).
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="configure">
    /// Optional delegate to customise <see cref="RedisL2Options"/>. When <see langword="null"/>
    /// the defaults are used.
    /// </param>
    /// <returns>The same <paramref name="builder"/> to allow further chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Effective L2 key format:</b> <c>{KeyPrefix}v2:{user-key}</c>
    /// </para>
    /// <para>
    /// <see cref="RedisL2Options.KeyPrefix"/> is passed to
    /// <c>Microsoft.Extensions.Caching.StackExchangeRedis</c> as its <c>InstanceName</c>.
    /// That library (v10+) prepends the instance name and a schema-version marker (<c>v2:</c>)
    /// to every key it writes to Redis. For example, a cache entry with user key
    /// <c>"myservice:order:42"</c> and <c>KeyPrefix = "myapp"</c> will be stored in Redis
    /// as <c>"myappv2:myservice:order:42"</c>.
    /// </para>
    /// <para>
    /// When <see cref="RedisL2Options.KeyPrefix"/> is empty (the default) the effective
    /// Redis key is <c>"v2:{user-key}"</c>.
    /// </para>
    /// <para>
    /// No additional prefix is applied by FusionCache — the FusionCache
    /// <c>CacheKeyPrefix</c> option is not set by <c>AddSharedKernelCaching</c> or
    /// <c>AddRedisL2</c>. There is no double-prefixing: the layout is
    /// <c>[InstanceName][v2:][user-key]</c>, contributed by three distinct layers
    /// (configuration, Redis library, and caller), each exactly once.
    /// </para>
    /// </remarks>
    public static ICachingBuilder AddRedisL2(
        this ICachingBuilder builder,
        string connectionString,
        Action<RedisL2Options>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new RedisL2Options { ConnectionString = connectionString };
        configure?.Invoke(options);

        var services = builder.Services;

        // Register the shared IConnectionMultiplexer singleton if not already registered.
        // This allows RedisChannelService and RedisHashService to reuse the same connection.
        var configOptions = ConfigurationOptions.Parse(options.ConnectionString);
        configOptions.ConnectTimeout = options.ConnectTimeoutMs;
        configOptions.AbortOnConnectFail = false;

        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(configOptions));

        // Register the Redis IDistributedCache for FusionCache L2 storage.
        services.AddStackExchangeRedisCache(redisOptions =>
        {
            redisOptions.Configuration = options.ConnectionString;
            if (!string.IsNullOrEmpty(options.KeyPrefix))
                redisOptions.InstanceName = options.KeyPrefix;
        });

        // Wire the registered IDistributedCache into FusionCache as L2 +
        // register the backplane for cross-node invalidation notifications.
        //
        // IMPORTANT (Phase 18 AOT fix): Do NOT call .WithSystemTextJsonSerializer() here.
        // AddSharedKernelCaching already registered an IFusionCacheSerializer (potentially
        // configured with the user's SerializerContext for NativeAOT safety) via
        // WithRegisteredSerializer(). Calling .WithSystemTextJsonSerializer() again would
        // silently overwrite that registration with a reflection-based default, causing
        // NativeAOT builds to fail at runtime.
        //
        // .WithRegisteredSerializer() instructs FusionCache to resolve IFusionCacheSerializer
        // from the DI container, picking up whatever AddSharedKernelCaching (and optionally
        // AddBrotliCompression) registered — whether the plain STJ serializer or a BrotliCacheSerializer
        // decorator wrapping it.
        services
            .AddFusionCache()
            .WithRegisteredDistributedCache()
            .WithRegisteredSerializer()
            .WithStackExchangeRedisBackplane(backplaneOptions =>
            {
                backplaneOptions.Configuration = options.ConnectionString;
            });

        // Register Polly circuit breaker pipeline only when explicitly enabled.
        // When Enabled = false (the default), no Polly types are registered and
        // all existing behavior is preserved unchanged.
        if (options.CircuitBreaker.Enabled)
        {
            var cbOpts = options.CircuitBreaker;
            services.TryAddSingleton(_ =>
                new ResiliencePipelineBuilder()
                    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                    {
                        // Polly v8 uses FailureRatio (0.0–1.0) + MinimumThroughput.
                        // We map the count-based FailureThreshold intent as follows:
                        //   MinimumThroughput = FailureThreshold (minimum calls before evaluation)
                        //   FailureRatio = 1.0 (circuit opens when ALL MinimumThroughput calls fail)
                        // This matches the semantic: "N failures within the window opens the circuit".
                        FailureRatio = 1.0,
                        MinimumThroughput = cbOpts.FailureThreshold,
                        SamplingDuration = cbOpts.SamplingDuration,
                        BreakDuration = cbOpts.BreakDuration,
                        ShouldHandle = new PredicateBuilder().Handle<Exception>()
                    })
                    .Build());
        }

        return builder;
    }
}
