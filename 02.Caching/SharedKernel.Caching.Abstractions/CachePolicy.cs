namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Describes the caching behaviour for a single entry: L1 and L2 TTLs, optional tags for
/// group-invalidation, fail-safe activation, and eager-refresh threshold.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CachePolicy"/> is a sealed immutable record. All customisation is performed via
/// the fluent factory methods <see cref="For"/>, <see cref="WithTags"/>, and
/// <see cref="WithEagerRefresh"/>. Do not subclass or mutate after construction.
/// </para>
/// <para>
/// The <see cref="Default"/> preset is the recommended starting point.
/// Tune only when profiling confirms it is necessary.
/// </para>
/// </remarks>
public sealed record CachePolicy
{
    // -------------------------------------------------------------------------
    // Private constructor — all creation goes through factory methods.
    // -------------------------------------------------------------------------

    private CachePolicy() { }

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// Time-to-live for the L1 in-process memory cache.
    /// Defaults to <c>5 minutes</c>.
    /// </summary>
    public TimeSpan L1Duration { get; private init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Time-to-live for the L2 distributed Redis cache.
    /// Defaults to <c>30 minutes</c>.
    /// Ignored when no Redis L2 provider is registered.
    /// </summary>
    public TimeSpan L2Duration { get; private init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Whether the fail-safe mechanism is enabled. When <see langword="true"/>, a stale
    /// value may be served if the factory throws or times out, preventing complete unavailability.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool FailSafeEnabled { get; private init; } = true;

    /// <summary>
    /// Tags assigned to this cache entry. Used for group-invalidation via
    /// <c>ICacheService.RemoveByTagAsync</c>.
    /// Defaults to an empty array.
    /// </summary>
    public string[] Tags { get; private init; } = [];

    /// <summary>
    /// Fraction of the L1 TTL at which background eager-refresh is triggered.
    /// A value of <c>0.9</c> means refresh begins when 90 % of the TTL has elapsed.
    /// Set to <see langword="null"/> to disable eager refresh.
    /// Defaults to <c>0.9</c>.
    /// </summary>
    public double? EagerRefreshThreshold { get; private init; } = 0.9;

    // -------------------------------------------------------------------------
    // Presets
    // -------------------------------------------------------------------------

    /// <summary>
    /// Default policy: 5-minute L1, 30-minute L2, fail-safe on, 90 % eager-refresh threshold,
    /// no tags.
    /// </summary>
    public static readonly CachePolicy Default = new();

    /// <summary>
    /// A cache policy for truly static data that should never expire via TTL.
    /// Both L1 and L2 durations are set to <see cref="TimeSpan.MaxValue"/> and fail-safe is enabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Intended use case: truly static data such as reference tables, feature flag snapshots,
    /// and lookup codes that never change during the lifetime of a deployment.
    /// </para>
    /// <para>
    /// <strong>Explicit invalidation required.</strong> Because TTL-based expiry will not occur,
    /// cached entries must be explicitly invalidated via
    /// <c>ICacheService.RemoveAsync</c> or <c>ICacheInvalidationBus</c> whenever the underlying
    /// data changes.
    /// </para>
    /// <para>
    /// <strong>Warning:</strong> Do not use this preset for any data that can change without an
    /// explicit invalidation signal. Using <see cref="NeverExpire"/> for mutable data will result
    /// in stale entries being served indefinitely.
    /// </para>
    /// <para>
    /// Eager refresh is intentionally disabled — there is nothing to refresh when no expiry
    /// is configured.
    /// </para>
    /// </remarks>
    public static CachePolicy NeverExpire { get; } = new()
    {
        L1Duration = TimeSpan.MaxValue,
        L2Duration = TimeSpan.MaxValue,
        FailSafeEnabled = true,
        EagerRefreshThreshold = null,
    };

    // -------------------------------------------------------------------------
    // Factory methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a policy with explicit L1 and L2 durations, retaining the default fail-safe
    /// and eager-refresh settings.
    /// </summary>
    /// <param name="l1">Time-to-live for the L1 in-process cache. Must be positive.</param>
    /// <param name="l2">Time-to-live for the L2 distributed cache. Must be positive.</param>
    /// <returns>A new <see cref="CachePolicy"/> with the specified durations.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="l1"/> or <paramref name="l2"/> is not positive.
    /// </exception>
    public static CachePolicy For(TimeSpan l1, TimeSpan l2)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(l1.Ticks, nameof(l1));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(l2.Ticks, nameof(l2));

        return new CachePolicy { L1Duration = l1, L2Duration = l2 };
    }

    /// <summary>
    /// Returns a copy of this policy with the supplied <paramref name="tags"/> applied.
    /// Tags are used for group-invalidation via <c>ICacheService.RemoveByTagAsync</c>.
    /// </summary>
    /// <param name="tags">One or more non-null tag strings.</param>
    /// <returns>A new <see cref="CachePolicy"/> with <see cref="Tags"/> set to <paramref name="tags"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="tags"/> is empty.</exception>
    public CachePolicy WithTags(params string[] tags)
    {
        if (tags.Length == 0)
            throw new ArgumentException("At least one tag must be supplied.", nameof(tags));

        return this with { Tags = tags };
    }

    /// <summary>
    /// Returns a copy of this policy with the eager-refresh threshold set to
    /// <paramref name="threshold"/>.
    /// </summary>
    /// <param name="threshold">
    /// A value in the range <c>(0, 1)</c> representing the fraction of L1 TTL at which
    /// background refresh is triggered. Use <c>0.9</c> for 90 % of TTL.
    /// </param>
    /// <returns>
    /// A new <see cref="CachePolicy"/> with <see cref="EagerRefreshThreshold"/> set.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="threshold"/> is not in the range <c>(0, 1)</c>.
    /// </exception>
    public CachePolicy WithEagerRefresh(double threshold = 0.9)
    {
        if (threshold is <= 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold,
                "Eager refresh threshold must be in the range (0, 1) exclusive.");

        return this with { EagerRefreshThreshold = threshold };
    }

    /// <summary>
    /// Returns a copy of this policy with eager refresh disabled.
    /// </summary>
    public CachePolicy WithoutEagerRefresh() => this with { EagerRefreshThreshold = null };

    /// <summary>
    /// Returns a copy of this policy with fail-safe disabled.
    /// </summary>
    public CachePolicy WithFailSafeDisabled() => this with { FailSafeEnabled = false };
}
