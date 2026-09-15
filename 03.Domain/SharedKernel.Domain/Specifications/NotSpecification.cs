using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A specification satisfied by entities that do not satisfy the inner specification.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
/// <remarks>
/// <para>
/// An inner specification without criteria matches every entity, and its negation therefore matches none.
/// Includes, string includes and the tracking, split-query and include-deleted flags are carried over.
/// </para>
/// <para><b>Ordering, paging and <c>Distinct</c> are not carried over.</b></para>
/// </remarks>
public sealed class NotSpecification<T> : Specification<T>
{
    /// <summary>Negates <paramref name="spec"/>.</summary>
    /// <param name="spec">The specification to negate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <see langword="null"/>.</exception>
    public NotSpecification(Specification<T> spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Criteria is null)
        {
            AddCriteria(_ => false);
        }
        else
        {
            var parameter = spec.Criteria.Parameters[0];
            AddCriteria(Expression.Lambda<Func<T, bool>>(Expression.Not(spec.Criteria.Body), parameter));
        }

        CopyQueryShapeFrom(spec);
    }
}
