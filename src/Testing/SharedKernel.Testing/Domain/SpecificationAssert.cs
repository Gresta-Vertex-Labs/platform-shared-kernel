using SharedKernel.Domain.Specifications;

namespace SharedKernel.Testing.Domain;

/// <summary>
/// Static assertion helpers that evaluate an <see cref="ISpecification{T}"/>'s
/// <see cref="ISpecification{T}.Criteria"/> against an in-memory entity, without a database.
/// </summary>
/// <remarks>
/// Compiles <see cref="ISpecification{T}.Criteria"/> via <c>Expression&lt;TDelegate&gt;.Compile()</c>
/// on every call — reflection-based expression compilation, acceptable in this test-only package,
/// never acceptable in production code per <c>03.Domain</c>'s own documented constraint on
/// <c>Specification&lt;T&gt;.IsSatisfiedBy</c>.
/// </remarks>
public static class SpecificationAssert
{
    /// <summary>
    /// Asserts that <paramref name="entity"/> satisfies <paramref name="spec"/>'s criteria.
    /// A specification with <see langword="null"/> criteria always satisfies.
    /// </summary>
    /// <typeparam name="T">The entity type the specification applies to.</typeparam>
    /// <param name="spec">The specification to evaluate.</param>
    /// <param name="entity">The entity to test.</param>
    /// <exception cref="InvalidOperationException"><paramref name="entity"/> does not satisfy <paramref name="spec"/>.</exception>
    public static void Satisfies<T>(ISpecification<T> spec, T entity)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (!IsSatisfiedBy(spec, entity))
        {
            throw new InvalidOperationException(
                $"Expected entity to satisfy the specification's criteria, but it did not.");
        }
    }

    /// <summary>
    /// Asserts that <paramref name="entity"/> does NOT satisfy <paramref name="spec"/>'s criteria.
    /// </summary>
    /// <typeparam name="T">The entity type the specification applies to.</typeparam>
    /// <param name="spec">The specification to evaluate.</param>
    /// <param name="entity">The entity to test.</param>
    /// <exception cref="InvalidOperationException"><paramref name="entity"/> satisfies <paramref name="spec"/>.</exception>
    public static void DoesNotSatisfy<T>(ISpecification<T> spec, T entity)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (IsSatisfiedBy(spec, entity))
        {
            throw new InvalidOperationException(
                $"Expected entity NOT to satisfy the specification's criteria, but it did.");
        }
    }

    private static bool IsSatisfiedBy<T>(ISpecification<T> spec, T entity) =>
        spec.Criteria is null || spec.Criteria.Compile()(entity);
}
