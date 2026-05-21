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

    /// <summary>
    /// Idle time-to-live for the L1 in-process memory cache when sliding expiration is enabled.
    /// When set, the L1 entry's expiry is reset each time the entry is accessed, up to the
    /// absolute ceiling imposed by <see cref="L1Duration"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>L1 only.</strong> Sliding expiration is mapped to the in-process
    /// <c>MemoryCache</c> <c>SlidingExpiration</c> property. The L2 Redis distributed
    /// cache does not support sliding expiry — the absolute <see cref="L2Duration"/> acts
    /// as the ceiling TTL for L2 entries.
    /// </para>
    /// <para>
    /// Defaults to <see langword="null"/> (no sliding expiry — pure absolute TTL only).
    /// </para>
    /// <para>
    /// Use <see cref="Sliding"/> to construct a policy with this property set.
    /// </para>
    /// </remarks>
    public TimeSpan? SlidingWindow { get; private init; } = null;

    /// <summary>
    /// Schema version used to differentiate cache keys across deployments.
    /// When greater than zero, <c>ICacheKeyProvider.BuildKey</c> appends a <c>:v{version}</c>
    /// suffix to the generated key (e.g. <c>order-svc:invoice:42:v3</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A value of <c>0</c> (the default) produces the original key format with no suffix —
    /// this is backward-compatible with all callers that do not call <see cref="WithVersion"/>.
    /// </para>
    /// <para>
    /// Use <see cref="WithVersion"/> to obtain a copy of this policy with a non-zero version.
    /// Pass <c>KeyVersion</c> to
    /// <c>ICacheKeyProvider.BuildKey(entity, id, version, extraSegments)</c> to bake
    /// the version into the final key string. Key versioning is the <strong>caller's
    /// responsibility</strong> — <c>ICacheService</c> method signatures do not change.
    /// </para>
    /// <para>
    /// <strong>Deployment workflow:</strong> increment the version in the code → deploy →
    /// the old key will expire naturally via its TTL — no explicit cache flush is required.
    /// </para>
    /// </remarks>
    public int KeyVersion { get; private init; } = 0;

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

    /// <summary>
    /// Returns a copy of this policy with the cache key schema version set to
    /// <paramref name="version"/>.
    /// </summary>
    /// <param name="version">
    /// A non-negative integer identifying the schema version. When greater than zero,
    /// <c>ICacheKeyProvider.BuildKey</c> appends a <c>:v{version}</c> suffix to the key
    /// (e.g. <c>order-svc:invoice:42:v3</c>). Pass <c>0</c> to revert to the original
    /// format with no suffix.
    /// </param>
    /// <returns>
    /// A new <see cref="CachePolicy"/> with <see cref="KeyVersion"/> set to
    /// <paramref name="version"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="version"/> is negative.
    /// </exception>
    /// <remarks>
    /// Chains correctly alongside <see cref="WithTags"/>, <see cref="WithEagerRefresh"/>,
    /// and <see cref="Sliding"/>:
    /// <code>
    /// CachePolicy.Default.WithVersion(3).WithTags("entity:invoice")
    /// </code>
    /// </remarks>
    public CachePolicy WithVersion(int version)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version, nameof(version));
        return this with { KeyVersion = version };
    }

    // -------------------------------------------------------------------------
    // Sliding-expiration factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a policy with sliding (idle) expiration for L1.
    /// The entry's L1 expiry resets on each access; the absolute ceiling is
    /// <see cref="Default"/>'s <see cref="L1Duration"/> (5 minutes) for L1
    /// and <see cref="Default"/>'s <see cref="L2Duration"/> (30 minutes) for L2.
    /// </summary>
    /// <param name="window">
    /// The idle TTL for the L1 in-process cache. The entry expires after this duration
    /// elapses without any access. Must be positive.
    /// </param>
    /// <returns>
    /// A new <see cref="CachePolicy"/> with <see cref="SlidingWindow"/> set to
    /// <paramref name="window"/> and absolute durations equal to <see cref="Default"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>L1 only.</strong> Sliding expiration applies only to the L1 in-process
    /// <c>MemoryCache</c>. The L2 Redis distributed cache does not support sliding expiry;
    /// L2 entries expire at the absolute <see cref="L2Duration"/> ceiling.
    /// </para>
    /// <para>
    /// <strong>Incompatible with <see cref="NeverExpire"/>.</strong> Combining a non-null
    /// <see cref="SlidingWindow"/> with <see cref="NeverExpire"/> (which sets
    /// <see cref="L1Duration"/> to <see cref="TimeSpan.MaxValue"/>) is invalid and will
    /// throw at the provider level (e.g., <c>FusionCacheService</c>).
    /// </para>
    /// <para>
    /// Use <see cref="CachePolicy.WithTags"/> after this factory to add tag-based
    /// group invalidation: <c>CachePolicy.Sliding(window).WithTags("tag")</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="window"/> is not positive.
    /// </exception>
    public static CachePolicy Sliding(TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(window.Ticks, nameof(window));

        return new CachePolicy
        {
            L1Duration = Default.L1Duration,
            L2Duration = Default.L2Duration,
            SlidingWindow = window,
        };
    }
}
