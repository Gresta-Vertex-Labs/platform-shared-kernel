namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Fluent extension methods for composing <see cref="Specification{T}"/> instances.
/// </summary>
public static class SpecificationExtensions
{
    /// <summary>
    /// Combines <paramref name="left"/> and <paramref name="right"/> into an
    /// <see cref="AndSpecification{T}"/> (logical AND).
    /// </summary>
    public static AndSpecification<T> And<T>(this Specification<T> left, Specification<T> right) =>
        new(left, right);

    /// <summary>
    /// Combines <paramref name="left"/> and <paramref name="right"/> into an
    /// <see cref="OrSpecification{T}"/> (logical OR).
    /// </summary>
    public static OrSpecification<T> Or<T>(this Specification<T> left, Specification<T> right) =>
        new(left, right);

    /// <summary>
    /// Wraps <paramref name="spec"/> in a <see cref="NotSpecification{T}"/> that negates it.
    /// </summary>
    public static NotSpecification<T> Not<T>(this Specification<T> spec) => new(spec);
}
