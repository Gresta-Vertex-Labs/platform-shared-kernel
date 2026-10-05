using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Execution.Context;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.Redis.Options;
using SharedKernel.Idempotency.Redis.Store;

namespace SharedKernel.Idempotency.Redis.Extensions;

/// <summary><see cref="IServiceCollection"/> extension methods for registering the Redis idempotency store.</summary>
public static class RedisIdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RedisIdempotencyStore"/> as the <see cref="IIdempotencyStore"/> for every purpose
    /// <paramref name="purposes"/> selects, keyed by purpose.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="purposes">Selects the purposes, for example <c>p =&gt; p.ForRequests().ForMessages()</c>.</param>
    /// <param name="configure">Optional delegate to customise <see cref="RedisIdempotencyOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="purposes"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>AddRedisConnection</c> has not been called, no purpose was selected, or a store is already registered for a
    /// selected purpose.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Uses the shared <see cref="StackExchange.Redis.IConnectionMultiplexer"/> registered by <c>02.Caching.Redis.Core</c>'s
    /// <c>AddRedisConnection</c> — call it first. This package never constructs its own connection, so TLS, timeouts and
    /// the Redis readiness probe are the shared connection's.
    /// </para>
    /// <para>
    /// Registers <see cref="IRequestContextAccessor"/> unless one is already registered: every key is scoped by the
    /// tenant of the ambient request context, which the service's inbound adapters set.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddRedisIdempotency(
        this IServiceCollection services,
        Action<IdempotencyPurposeSelection> purposes,
        Action<RedisIdempotencyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(purposes);
        services.EnsureRedisConnectionRegistered(nameof(AddRedisIdempotency));

        var selected = IdempotencyServiceCollectionExtensions.SelectPurposes(purposes);

        services
            .AddOptions<RedisIdempotencyOptions>()
            .Configure(o => configure?.Invoke(o));

        foreach (var purpose in selected)
            services.AddIdempotencyStore<RedisIdempotencyStore>(purpose);

        services.TryAddSingleton<IRequestContextAccessor, RequestContextAccessor>();

        return services;
    }
}
