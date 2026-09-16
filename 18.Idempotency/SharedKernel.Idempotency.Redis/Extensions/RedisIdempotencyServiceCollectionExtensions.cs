using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Idempotency.Redis.KeyStore;
using SharedKernel.Idempotency.Redis.MessageStore;
using SharedKernel.Idempotency.Redis.Options;
using SharedKernel.Idempotency.Redis.Startup;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.Abstractions.TenantContext;

namespace SharedKernel.Idempotency.Redis.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering the Redis-backed
/// idempotency stores.
/// </summary>
public static class RedisIdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RedisRequestIdempotencyStore"/> as <see cref="IRequestIdempotencyStore"/>,
    /// and <see cref="RedisIdempotencyMessageStore"/> as <see cref="IIdempotencyStore"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional delegate to customise <see cref="RedisIdempotencyOptions"/>. When
    /// <see langword="null"/> the defaults are used.
    /// </param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Resolves the shared <see cref="StackExchange.Redis.IConnectionMultiplexer"/> registered by
    /// <c>02.Caching.Redis.Core</c>'s <c>AddRedisConnection</c> — call that method (directly, or
    /// transitively via any <c>02.Caching.Redis.*</c> package) before this one. This package never
    /// constructs its own multiplexer.
    /// </para>
    /// <para>
    /// Registers a startup-time <see cref="Microsoft.Extensions.Hosting.IHostedService"/>
    /// (<see cref="IdempotencyTenantAccessorStartupValidator"/>) that throws
    /// <see cref="InvalidOperationException"/> at <c>IHost.StartAsync()</c> if no
    /// <see cref="ITenantContextAccessor"/> has been registered — fail-fast, not first-use.
    /// </para>
    /// <para>
    /// Also registers <see cref="IdempotencyOptions"/> (<c>07.Messaging.Abstractions</c>) via
    /// <see cref="Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions.AddOptions{TOptions}(IServiceCollection)"/>
    /// with defaults if not already registered — <see cref="RedisIdempotencyMessageStore"/> reads
    /// <see cref="IdempotencyOptions.ExpiryWindow"/> as its full-retention value (D-08). If the
    /// consuming service already registers <see cref="IdempotencyOptions"/> itself (e.g. via
    /// <c>MessagingBusBuilder.WithIdempotency(...)</c>), that registration is left untouched —
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService}(IServiceCollection, TService)"/>-style
    /// first-registration-wins semantics are honored through <c>AddOptions</c>'s own idempotent
    /// registration behavior.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelRedisIdempotency(
        this IServiceCollection services,
        Action<RedisIdempotencyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddOptions<RedisIdempotencyOptions>()
            .Configure(o => configure?.Invoke(o))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Ensures IOptions<IdempotencyOptions> resolves even when the consuming service never
        // called 07.Messaging's MessagingBusBuilder.WithIdempotency(...) — AddOptions is
        // idempotent, so a prior registration (with the consumer's own configured ExpiryWindow)
        // is left untouched.
        services.AddOptions<IdempotencyOptions>();

        services.AddScoped<IRequestIdempotencyStore, RedisRequestIdempotencyStore>();
        services.AddScoped<IIdempotencyStore, RedisIdempotencyMessageStore>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, IdempotencyTenantAccessorStartupValidator>());

        return services;
    }
}
