namespace SharedKernel.Domain.Policies;

/// <summary>
/// Fluent extension methods for composing <see cref="IPolicy{T}"/> instances.
/// </summary>
public static class PolicyExtensions
{
    /// <summary>
    /// Combines <paramref name="left"/> and <paramref name="right"/> into an
    /// <see cref="AndPolicy{T}"/> that is compliant only when both sub-policies are compliant.
    /// </summary>
    public static AndPolicy<T> And<T>(this IPolicy<T> left, IPolicy<T> right) => new(left, right);

    /// <summary>
    /// Combines <paramref name="left"/> and <paramref name="right"/> into an
    /// <see cref="OrPolicy{T}"/> that is compliant when at least one sub-policy is compliant.
    /// </summary>
    public static OrPolicy<T> Or<T>(this IPolicy<T> left, IPolicy<T> right) => new(left, right);

    /// <summary>
    /// Wraps <paramref name="policy"/> in a <see cref="NotPolicy{T}"/> that inverts its compliance.
    /// </summary>
    public static NotPolicy<T> Not<T>(this IPolicy<T> policy) => new(policy);
}
