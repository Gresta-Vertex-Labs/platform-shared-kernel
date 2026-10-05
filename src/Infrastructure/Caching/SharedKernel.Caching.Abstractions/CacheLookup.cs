using System.Diagnostics.CodeAnalysis;

namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// The outcome of a cache read: either a hit carrying the stored value, or a miss.
/// </summary>
/// <remarks>
/// <para>
/// A hit can carry <see langword="null"/> or <see langword="default"/>: a cached "not found" result
/// and a cached <c>0</c> are hits, not misses. Check <see cref="IsHit"/> before reading
/// <see cref="Value"/>, or use <see cref="TryGetValue"/>.
/// </para>
/// <para>
/// <see langword="default"/>(<see cref="CacheLookup{T}"/>) is a miss. Implementations of
/// <see cref="ICacheService"/> create lookups with <see cref="Hit"/> and <see cref="Miss"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// CacheLookup&lt;Customer?&gt; lookup = await cache.TryGetAsync&lt;Customer?&gt;(key, ct);
/// string state = lookup switch
/// {
///     { IsHit: false } =&gt; "not cached",
///     { Value: null } =&gt; "cached as not found",
///     _ =&gt; "cached",
/// };
/// </code>
/// </example>
/// <typeparam name="T">The type of the cached value.</typeparam>
public readonly struct CacheLookup<T> : IEquatable<CacheLookup<T>>
{
    private readonly T _value;

    private CacheLookup(T value)
    {
        _value = value;
        IsHit = true;
    }

    /// <summary>Gets a lookup that found nothing.</summary>
    public static CacheLookup<T> Miss => default;

    /// <summary>Gets a value indicating whether the key was present in the cache.</summary>
    public bool IsHit { get; }

    /// <summary>Gets the cached value.</summary>
    /// <exception cref="InvalidOperationException">The lookup is a miss.</exception>
    public T Value => IsHit
        ? _value
        : throw new InvalidOperationException("The cache lookup was a miss and has no value. Check IsHit first.");

    /// <summary>Creates a lookup that found <paramref name="value"/>.</summary>
    /// <param name="value">The cached value, which may be <see langword="null"/>.</param>
    /// <returns>A hit carrying <paramref name="value"/>.</returns>
    public static CacheLookup<T> Hit(T value) => new(value);

    /// <summary>Gets the cached value when the lookup is a hit.</summary>
    /// <param name="value">The cached value on a hit; <see langword="default"/> on a miss.</param>
    /// <returns><see langword="true"/> on a hit.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return IsHit;
    }

    /// <summary>Gets the cached value on a hit, or <paramref name="fallback"/> on a miss.</summary>
    /// <param name="fallback">The value to return on a miss.</param>
    /// <returns>The cached value or <paramref name="fallback"/>.</returns>
    public T GetValueOrDefault(T fallback) => IsHit ? _value : fallback;

    /// <inheritdoc />
    public bool Equals(CacheLookup<T> other) =>
        IsHit == other.IsHit && (!IsHit || EqualityComparer<T>.Default.Equals(_value, other._value));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CacheLookup<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => IsHit ? HashCode.Combine(true, _value) : 0;

    /// <inheritdoc />
    public override string ToString() => IsHit ? $"Hit({_value})" : "Miss";

    /// <summary>Compares two lookups for equality.</summary>
    /// <param name="left">The first lookup.</param>
    /// <param name="right">The second lookup.</param>
    /// <returns><see langword="true"/> when both are misses, or both are hits with equal values.</returns>
    public static bool operator ==(CacheLookup<T> left, CacheLookup<T> right) => left.Equals(right);

    /// <summary>Compares two lookups for inequality.</summary>
    /// <param name="left">The first lookup.</param>
    /// <param name="right">The second lookup.</param>
    /// <returns><see langword="true"/> when the lookups differ.</returns>
    public static bool operator !=(CacheLookup<T> left, CacheLookup<T> right) => !left.Equals(right);
}
