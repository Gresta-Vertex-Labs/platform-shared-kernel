namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// A startup routine that fills the cache before the service takes traffic, so the first requests
/// after a deployment do not all miss at once.
/// </summary>
/// <remarks>
/// <para>
/// The provider runs every registered strategy once at startup, in ascending <see cref="Order"/>.
/// With the FusionCache provider, register a strategy with <c>AddCacheWarmup&lt;TStrategy&gt;()</c>
/// and set <c>CachingOptions.WaitForWarmup</c> to hold readiness until all strategies finish.
/// </para>
/// <para>
/// A failing strategy must not stop the host: the provider logs the exception and continues with the
/// next strategy. Honour the cancellation token, which is cancelled when the host shuts down.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class CurrencyWarmup(ICurrencyRepository currencies, ICacheKeyProvider keys) : ICacheWarmupStrategy
/// {
///     public string Name =&gt; "currencies";
///     public int Order =&gt; 0;
///
///     public async ValueTask WarmupAsync(ICacheService cache, CancellationToken ct)
///     {
///         foreach (Currency currency in await currencies.ListAsync(ct))
///             await cache.SetAsync(keys.BuildKey("currency", currency.Code), currency, CachePolicy.NeverExpire, ct);
///     }
/// }
/// </code>
/// </example>
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
