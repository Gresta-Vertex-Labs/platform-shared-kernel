using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.Abstractions.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for configuring
/// <see cref="CachingCoreOptions"/> without depending on any caching provider package.
/// </summary>
public static class CachingCoreOptionsExtensions
{
    /// <summary>
    /// Configures <see cref="CachingCoreOptions"/> on the service collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this extension when a service needs to set <see cref="CachingCoreOptions.ServiceName"/>
    /// (or other core caching options) but does <b>not</b> depend on
    /// <c>SharedKernel.Caching.FusionCache</c> or call <c>AddSharedKernelCaching</c>.
    /// Typical consumers are background workers that use Redis-only services such as
    /// <c>IDistributedLockService</c> or <c>ICacheInvalidationBus</c>.
    /// </para>
    /// <para>
    /// <b>Provider-free:</b> this method has zero dependencies on FusionCache,
    /// StackExchange.Redis, or any other provider package. It imports only
    /// <c>Microsoft.Extensions.DependencyInjection</c> and
    /// <c>Microsoft.Extensions.Options</c>.
    /// </para>
    /// <para>
    /// <b>Additive configuration:</b> <see cref="IOptions{TOptions}"/> configuration is
    /// additive. A service calling both <c>AddCachingCoreOptions</c> and
    /// <c>AddSharedKernelCaching</c> is valid; both delegates are applied (last-writer-wins
    /// for the same property).
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">
    /// A delegate that mutates the <see cref="CachingCoreOptions"/> instance.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/> to allow further chaining.</returns>
    /// <example>
    /// <code>
    /// // Redis-only background worker — no FusionCache dependency.
    /// services.AddCachingCoreOptions(o => o.ServiceName = "worker-service")
    ///         .AddRedisDistributedLocking(connectionString);
    /// </code>
    /// </example>
    public static IServiceCollection AddCachingCoreOptions(
        this IServiceCollection services,
        Action<CachingCoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        return services;
    }
}
