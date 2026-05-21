namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Defines a single cache warmup strategy that pre-populates L1 cache entries at service startup,
/// before the host signals readiness to receive traffic.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface to provide a named, ordered warmup routine that executes during
/// the startup phase. Multiple strategies can be registered and will be executed in ascending
/// <see cref="Order"/> sequence.
/// </para>
/// <para>
/// Register strategies via <c>ICachingBuilder.AddCacheWarmup&lt;TStrategy&gt;()</c>. Set
/// <c>CachingOptions.WaitForWarmup = true</c> to ensure the pod does not receive Kubernetes
/// traffic until all warmup strategies complete.
/// </para>
/// <para>
/// A failing strategy must not crash the host. The runner (<c>CacheWarmupHostedService</c>)
/// catches all exceptions per strategy, logs them at <see cref="Microsoft.Extensions.Logging.LogLevel.Error"/>,
/// and continues with the next strategy.
/// </para>
/// </remarks>
public interface ICacheWarmupStrategy
{
    /// <summary>
    /// Gets the human-readable name of this warmup strategy. Used in log messages and telemetry.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the execution order for this strategy relative to other registered strategies.
    /// Strategies are executed in ascending order (lowest value runs first).
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Executes the cache warmup logic, pre-populating <paramref name="cache"/> with the
    /// entries required by this strategy.
    /// </summary>
    /// <param name="cache">The cache service to warm up.</param>
    /// <param name="ct">A cancellation token that is cancelled when the host is stopping.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the warmup is done.</returns>
    ValueTask WarmupAsync(ICacheService cache, CancellationToken ct);
}
