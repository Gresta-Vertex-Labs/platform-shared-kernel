namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Lets a <c>GetOrSetAsync</c> factory decide, after computing its value, whether and for how
/// long that value is cached.
/// </summary>
/// <remarks>
/// <para>
/// The value the factory returns is always handed to the caller and to every concurrent caller
/// waiting on the same key. The context only controls what is written to the cache.
/// </para>
/// <para>
/// Typical use: a factory that returns a failed <c>Result</c> calls <see cref="SkipCaching"/> so
/// the failure is not served to later callers, while still keeping stampede protection for the
/// successful path.
/// </para>
/// <para>
/// <see cref="ICacheService"/> implementations create one context per factory run and apply its
/// decision after the factory returns. A decision made after the factory has returned has no effect.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var rate = await cache.GetOrSetAsync(
///     keys.BuildKey("fx-rate", pair),
///     async (context, token) =&gt;
///     {
///         FxRate quote = await rates.GetAsync(pair, token);
///         if (quote.IsIndicative)
///             context.SetDurations(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)); // refresh soon
///         return quote;
///     },
///     CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)),
///     ct);
/// </code>
/// </example>
public sealed class CacheFactoryContext
{
    /// <summary>Creates a context for one factory execution.</summary>
    /// <param name="key">The cache key being computed.</param>
    /// <param name="policy">The policy the caller requested.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> is <see langword="null"/>.</exception>
    public CacheFactoryContext(string key, CachePolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(policy);

        Key = key;
        Policy = policy;
    }

    /// <summary>Gets the cache key being computed.</summary>
    public string Key { get; }

    /// <summary>Gets the policy the caller requested.</summary>
    public CachePolicy Policy { get; }

    /// <summary>Gets a value indicating whether the factory asked not to cache its value.</summary>
    public bool IsCachingSkipped { get; private set; }

    /// <summary>
    /// Gets the memory-cache duration the factory chose, or <see langword="null"/> to use
    /// <see cref="Policy"/>.
    /// </summary>
    public TimeSpan? L1DurationOverride { get; private set; }

    /// <summary>
    /// Gets the distributed-cache duration the factory chose, or <see langword="null"/> to use
    /// <see cref="Policy"/>.
    /// </summary>
    public TimeSpan? L2DurationOverride { get; private set; }

    /// <summary>
    /// Returns the computed value to the callers without writing it to either cache layer.
    /// An existing entry for the key is left untouched.
    /// </summary>
    public void SkipCaching() => IsCachingSkipped = true;

    /// <summary>
    /// Caches the computed value for the given durations instead of the policy's durations.
    /// </summary>
    /// <param name="l1Duration">The memory-cache duration. Must be positive and not exceed <paramref name="l2Duration"/>.</param>
    /// <param name="l2Duration">The distributed-cache duration. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">A duration is not positive, or <paramref name="l1Duration"/> exceeds <paramref name="l2Duration"/>.</exception>
    public void SetDurations(TimeSpan l1Duration, TimeSpan l2Duration)
    {
        CachePolicy.ValidateDurations(l1Duration, l2Duration);
        L1DurationOverride = l1Duration;
        L2DurationOverride = l2Duration;
    }
}
