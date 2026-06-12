using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Polly.CircuitBreaker;

namespace SharedKernel.Caching.Redis.Core.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering an opt-in Polly v8
/// <see cref="ResiliencePipeline"/> circuit breaker for Redis operations.
/// </summary>
public static class RedisCircuitBreakerExtensions
{
    /// <summary>
    /// Registers a Polly v8 <see cref="ResiliencePipeline"/> singleton configured as a
    /// circuit breaker, but only when <see cref="RedisCircuitBreakerOptions.Enabled"/> is
    /// <see langword="true"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional delegate to customise <see cref="RedisCircuitBreakerOptions"/>. When
    /// <see langword="null"/> the defaults are used (<see cref="RedisCircuitBreakerOptions.Enabled"/>
    /// is <see langword="false"/> by default, so nothing is registered).
    /// </param>
    /// <returns>The same <paramref name="services"/> to allow further chaining.</returns>
    /// <remarks>
    /// <para>
    /// When <see cref="RedisCircuitBreakerOptions.Enabled"/> is <see langword="false"/> (the
    /// default), no <see cref="ResiliencePipeline"/> is registered and existing behavior is
    /// preserved unchanged.
    /// </para>
    /// <para>
    /// <b>Count-based semantics via ratio API:</b> Polly v8 uses ratio-based circuit breaking
    /// (<c>FailureRatio</c> 0.0–1.0 + <c>MinimumThroughput</c>). Count-based behaviour is
    /// emulated by setting <c>FailureRatio = 1.0</c> and
    /// <c>MinimumThroughput = RedisCircuitBreakerOptions.FailureThreshold</c> — "all calls in
    /// the window must fail AND the threshold count must be reached."
    /// </para>
    /// <para>
    /// The pipeline is registered via
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService}(IServiceCollection, Func{IServiceProvider, TService})"/> —
    /// calling this method multiple times (e.g., once per capability package) does not throw
    /// or duplicate the registration.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddRedisCircuitBreaker(
        this IServiceCollection services,
        Action<RedisCircuitBreakerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RedisCircuitBreakerOptions();
        configure?.Invoke(options);

        if (!options.Enabled)
        {
            return services;
        }

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
                    MinimumThroughput = options.FailureThreshold,
                    SamplingDuration = options.SamplingDuration,
                    BreakDuration = options.BreakDuration,
                    ShouldHandle = new PredicateBuilder().Handle<Exception>()
                })
                .Build());

        return services;
    }
}
