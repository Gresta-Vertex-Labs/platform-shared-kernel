using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Describes how one cache entry is stored: memory (L1) and distributed (L2) durations, tags,
/// fail-safe, factory timeouts, eager refresh, expiration jitter, and whether the distributed
/// layer is used at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Immutable and always valid.</b> Start from <see cref="Default"/>, <see cref="NeverExpire"/> or
/// <see cref="For(TimeSpan, TimeSpan)"/> and derive variants with the <c>With…</c> methods. Each
/// returns a new instance and validates its arguments, so an invalid policy cannot exist. Policies
/// are safe to keep in <see langword="static"/> fields and share across threads. Two policies are
/// equal when every setting and every tag, in order, is equal.
/// </para>
/// <para>
/// <b>Choosing durations.</b> L1 should be short enough that instances converge quickly after a
/// change, and L2 long enough to protect the source of truth. L1 may not exceed L2. L2 settings are
/// ignored when no distributed cache is configured.
/// </para>
/// <para>
/// <b>Background work.</b> Eager refresh and a soft factory timeout run the factory after the caller
/// has returned. Turn both off (<see cref="WithoutEagerRefresh"/>, <c>WithFactoryTimeouts(null, null)</c>)
/// when the factory uses request-scoped services such as a scoped <c>DbContext</c>.
/// </para>
/// <para>
/// <b>Tenants.</b> Pass unscoped policies to <see cref="ITenantCacheService"/>, which applies
/// <see cref="ForTenant"/>. Tags starting with <c>@</c> are reserved for tenant scoping.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Declare once, reuse everywhere.
/// private static readonly CachePolicy ProductPolicy = CachePolicy
///     .For(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(30))
///     .WithTags("products")
///     .WithFailSafe(TimeSpan.FromHours(2))
///     .WithFactoryTimeouts(softTimeout: TimeSpan.FromMilliseconds(200), hardTimeout: TimeSpan.FromSeconds(2))
///     .WithJitter(TimeSpan.FromSeconds(20));
/// </code>
/// </example>
public sealed record CachePolicy
{
    private IReadOnlyList<string> _tags = [];

    private CachePolicy() { }

    /// <summary>Gets how long an entry stays in the memory cache. Defaults to 5 minutes.</summary>
    /// <remarks><see cref="TimeSpan.MaxValue"/> means the entry never expires by time.</remarks>
    public TimeSpan L1Duration { get; private init; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets how long an entry stays in the distributed cache. Defaults to 30 minutes.</summary>
    /// <remarks><see cref="TimeSpan.MaxValue"/> means the entry never expires by time.</remarks>
    public TimeSpan L2Duration { get; private init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gets the tags attached to the entry, used to remove groups of entries with
    /// <see cref="ICacheService.RemoveByTagAsync"/>. Defaults to none.
    /// </summary>
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        private init => _tags = value;
    }

    /// <summary>
    /// Gets a value indicating whether an expired entry may be served when refreshing it fails or
    /// times out. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Fail-safe keeps a service answering while its source of truth is down, at the price of stale
    /// data. Disable it (<see cref="WithoutFailSafe"/>) where stale data is unsafe, such as
    /// authorization or a tenant's suspended status.
    /// </remarks>
    public bool IsFailSafeEnabled { get; private init; } = true;

    /// <summary>
    /// Gets how long past its duration an expired entry may still be served by fail-safe, or
    /// <see langword="null"/> for the provider's default.
    /// </summary>
    public TimeSpan? FailSafeMaxDuration { get; private init; }

    /// <summary>
    /// Gets how long a caller waits for a factory before being served a stale entry, while the
    /// factory keeps running in the background, or <see langword="null"/> for no limit. Applies
    /// only when fail-safe is enabled and a stale entry exists.
    /// </summary>
    public TimeSpan? FactorySoftTimeout { get; private init; }

    /// <summary>
    /// Gets how long a caller waits for a factory before the call fails, even without a stale
    /// entry, or <see langword="null"/> for no limit.
    /// </summary>
    /// <remarks>
    /// The caller stops waiting, but the factory may keep running in the background and store its
    /// result when it completes.
    /// </remarks>
    public TimeSpan? FactoryHardTimeout { get; private init; }

    /// <summary>
    /// Gets the fraction of <see cref="L1Duration"/> after which a read refreshes the entry in the
    /// background, or <see langword="null"/> to disable eager refresh. Defaults to <c>0.9</c>.
    /// </summary>
    public double? EagerRefreshThreshold { get; private init; } = 0.9;

    /// <summary>
    /// Gets the upper bound of a random extra duration added to each entry so that entries written
    /// together do not expire together, or <see langword="null"/> for no jitter.
    /// </summary>
    public TimeSpan? JitterMaxDuration { get; private init; }

    /// <summary>
    /// Gets a value indicating whether the entry lives only in the memory cache of this process,
    /// never in the distributed cache. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Use it for values that are cheap to rebuild, specific to one instance, or not serializable.
    /// Other instances neither see these entries nor evict their own copies when this one is written.
    /// </remarks>
    public bool IsLocalOnly { get; private init; }

    /// <summary>
    /// Gets a value indicating whether the tags have been scoped to a tenant with
    /// <see cref="ForTenant"/>.
    /// </summary>
    public bool IsTenantScoped { get; private init; }

    /// <summary>Gets the default policy: 5-minute L1, 30-minute L2, fail-safe on, eager refresh at 90%.</summary>
    public static CachePolicy Default { get; } = new();

    /// <summary>
    /// Gets a policy for reference data that changes only with an explicit removal: no time-based
    /// expiry and no eager refresh.
    /// </summary>
    /// <remarks>
    /// Remove or expire these entries whenever the source data changes; otherwise stale data is
    /// served until the process restarts or the distributed entry is evicted.
    /// </remarks>
    public static CachePolicy NeverExpire { get; } = new()
    {
        L1Duration = TimeSpan.MaxValue,
        L2Duration = TimeSpan.MaxValue,
        EagerRefreshThreshold = null,
    };

    /// <summary>Creates a policy with the given durations and the default settings otherwise.</summary>
    /// <param name="l1Duration">The memory-cache duration. Must be positive and not exceed <paramref name="l2Duration"/>.</param>
    /// <param name="l2Duration">The distributed-cache duration. Must be positive.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A duration is not positive, or <paramref name="l1Duration"/> exceeds <paramref name="l2Duration"/>.</exception>
    public static CachePolicy For(TimeSpan l1Duration, TimeSpan l2Duration)
    {
        ValidateDurations(l1Duration, l2Duration);
        return new CachePolicy { L1Duration = l1Duration, L2Duration = l2Duration };
    }

    /// <summary>Creates a policy that uses <paramref name="duration"/> for both cache layers.</summary>
    /// <param name="duration">The duration. Must be positive.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is not positive.</exception>
    public static CachePolicy For(TimeSpan duration) => For(duration, duration);

    /// <summary>Returns a copy with <paramref name="tags"/> replacing the current tags.</summary>
    /// <param name="tags">One or more tags. Each must be non-whitespace and must not start with <c>@</c>, which marks tenant tags.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tags"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="tags"/> is empty, or a tag is invalid.</exception>
    /// <exception cref="InvalidOperationException">The policy is already tenant-scoped.</exception>
    public CachePolicy WithTags(params string[] tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        EnsureNotTenantScoped();

        if (tags.Length == 0)
        {
            throw new ArgumentException("At least one tag must be supplied.", nameof(tags));
        }

        var copy = new string[tags.Length];
        for (int i = 0; i < tags.Length; i++)
        {
            string tag = tags[i];
            if (string.IsNullOrWhiteSpace(tag))
            {
                throw new ArgumentException("Tags must not be null or whitespace.", nameof(tags));
            }

            if (tag[0] == CacheKeyFormat.TenantMarker)
            {
                throw new ArgumentException(
                    $"Tag '{tag}' starts with '{CacheKeyFormat.TenantMarker}', which is reserved for tenant tags. Use ForTenant to scope tags to a tenant.",
                    nameof(tags));
            }

            copy[i] = tag;
        }

        return this with { Tags = Array.AsReadOnly(copy) };
    }

    /// <summary>
    /// Returns a copy whose tags are scoped to <paramref name="tenantId"/>, plus the tenant-wide tag
    /// every tenant entry carries (see <see cref="CacheKeyFormat"/>).
    /// </summary>
    /// <remarks>
    /// Applied automatically by <see cref="ITenantCacheService"/>. Call it directly only when writing
    /// tenant data through <see cref="ICacheService"/>, so the entry still responds to
    /// <see cref="ITenantCacheService.RemoveByTagAsync"/> and <see cref="ITenantCacheService.RemoveTenantAsync"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// CachePolicy.Default.WithTags("orders").ForTenant(tenantId).Tags  // ["@{tenantId}:orders", "@{tenantId}"]
    /// </code>
    /// </example>
    /// <remarks><see cref="ITenantCacheService"/> applies this automatically.</remarks>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>A new, tenant-scoped policy.</returns>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> is <see langword="default"/>.</exception>
    /// <exception cref="InvalidOperationException">The policy is already tenant-scoped.</exception>
    public CachePolicy ForTenant(TenantId tenantId)
    {
        if (tenantId.IsDefault)
        {
            throw new ArgumentException("The tenant identifier must not be default(TenantId).", nameof(tenantId));
        }

        EnsureNotTenantScoped();

        var scoped = new string[_tags.Count + 1];
        for (int i = 0; i < _tags.Count; i++)
        {
            scoped[i] = CacheKeyFormat.BuildTenantTag(tenantId, _tags[i]);
        }

        scoped[^1] = CacheKeyFormat.BuildTenantWideTag(tenantId);
        return this with { Tags = Array.AsReadOnly(scoped), IsTenantScoped = true };
    }

    /// <summary>Returns a copy with fail-safe enabled and the given maximum stale duration.</summary>
    /// <param name="maxDuration">How long past its duration an entry may be served. Must be positive.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDuration"/> is not positive.</exception>
    public CachePolicy WithFailSafe(TimeSpan maxDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxDuration, TimeSpan.Zero);
        return this with { IsFailSafeEnabled = true, FailSafeMaxDuration = maxDuration };
    }

    /// <summary>Returns a copy with fail-safe disabled and no soft factory timeout.</summary>
    /// <returns>A new policy.</returns>
    public CachePolicy WithoutFailSafe() =>
        this with { IsFailSafeEnabled = false, FailSafeMaxDuration = null, FactorySoftTimeout = null };

    /// <summary>Returns a copy with the given factory timeouts.</summary>
    /// <param name="softTimeout">The soft timeout, or <see langword="null"/> for none. Must be positive and shorter than <paramref name="hardTimeout"/>.</param>
    /// <param name="hardTimeout">The hard timeout, or <see langword="null"/> for none. Must be positive.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A timeout is not positive, or the soft timeout is not shorter than the hard timeout.</exception>
    /// <exception cref="InvalidOperationException">A soft timeout is set while fail-safe is disabled.</exception>
    public CachePolicy WithFactoryTimeouts(TimeSpan? softTimeout, TimeSpan? hardTimeout)
    {
        if (softTimeout is { } soft)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(soft, TimeSpan.Zero, nameof(softTimeout));
            if (!IsFailSafeEnabled)
            {
                throw new InvalidOperationException("A soft factory timeout needs fail-safe: without a stale entry there is nothing to serve.");
            }
        }

        if (hardTimeout is { } hard)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(hard, TimeSpan.Zero, nameof(hardTimeout));
            if (softTimeout is { } s && s >= hard)
            {
                throw new ArgumentOutOfRangeException(nameof(softTimeout), softTimeout, "The soft timeout must be shorter than the hard timeout.");
            }
        }

        return this with { FactorySoftTimeout = softTimeout, FactoryHardTimeout = hardTimeout };
    }

    /// <summary>Returns a copy that refreshes entries in the background after <paramref name="threshold"/> of <see cref="L1Duration"/>.</summary>
    /// <param name="threshold">A fraction in the open range (0, 1).</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="threshold"/> is outside (0, 1).</exception>
    public CachePolicy WithEagerRefresh(double threshold = 0.9)
    {
        if (threshold is <= 0 or >= 1 || double.IsNaN(threshold))
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "The eager refresh threshold must be in the open range (0, 1).");
        }

        return this with { EagerRefreshThreshold = threshold };
    }

    /// <summary>Returns a copy with eager refresh disabled.</summary>
    /// <returns>A new policy.</returns>
    public CachePolicy WithoutEagerRefresh() => this with { EagerRefreshThreshold = null };

    /// <summary>Returns a copy that adds up to <paramref name="maxDuration"/> of random extra duration to each entry.</summary>
    /// <param name="maxDuration">The maximum jitter. Must be positive.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDuration"/> is not positive.</exception>
    public CachePolicy WithJitter(TimeSpan maxDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxDuration, TimeSpan.Zero);
        return this with { JitterMaxDuration = maxDuration };
    }

    /// <summary>Returns a copy whose entries live only in this process's memory cache.</summary>
    /// <returns>A new policy.</returns>
    public CachePolicy LocalOnly() => this with { IsLocalOnly = true };

    /// <inheritdoc />
    public bool Equals(CachePolicy? other) =>
        other is not null
        && L1Duration == other.L1Duration
        && L2Duration == other.L2Duration
        && IsFailSafeEnabled == other.IsFailSafeEnabled
        && FailSafeMaxDuration == other.FailSafeMaxDuration
        && FactorySoftTimeout == other.FactorySoftTimeout
        && FactoryHardTimeout == other.FactoryHardTimeout
        && EagerRefreshThreshold == other.EagerRefreshThreshold
        && JitterMaxDuration == other.JitterMaxDuration
        && IsLocalOnly == other.IsLocalOnly
        && IsTenantScoped == other.IsTenantScoped
        && _tags.SequenceEqual(other._tags, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(L1Duration);
        hash.Add(L2Duration);
        hash.Add(IsFailSafeEnabled);
        hash.Add(FailSafeMaxDuration);
        hash.Add(FactorySoftTimeout);
        hash.Add(FactoryHardTimeout);
        hash.Add(EagerRefreshThreshold);
        hash.Add(JitterMaxDuration);
        hash.Add(IsLocalOnly);
        hash.Add(IsTenantScoped);
        foreach (string tag in _tags)
        {
            hash.Add(tag, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    internal static void ValidateDurations(TimeSpan l1Duration, TimeSpan l2Duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(l1Duration, TimeSpan.Zero, nameof(l1Duration));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(l2Duration, TimeSpan.Zero, nameof(l2Duration));

        if (l1Duration > l2Duration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(l1Duration),
                l1Duration,
                "The memory-cache duration must not exceed the distributed-cache duration.");
        }
    }

    private void EnsureNotTenantScoped()
    {
        if (IsTenantScoped)
        {
            throw new InvalidOperationException("The policy is already scoped to a tenant. Set tags before calling ForTenant.");
        }
    }
}
