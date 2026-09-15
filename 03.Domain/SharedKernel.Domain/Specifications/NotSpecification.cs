using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A specification matched by entities that do not satisfy its operand's criteria.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <remarks>
/// <para>
/// <b>Criteria.</b> The logical negation of the operand's criteria. An operand without criteria matches every
/// entity, so its negation has the criteria <c>_ =&gt; false</c> and matches none.
/// </para>
/// <para>
/// <b>Query shape.</b> Copies the operand's includes, string includes and tracking, split-query and
/// include-deleted flags.
/// </para>
/// <para>
/// <b>Pitfall.</b> Ordering, paging and <see cref="ISpecification{T}.IsDistinct"/> are never copied. Negation
/// applies to the criteria only: an operand that includes soft-deleted entities still bypasses the global
/// query filters, tenant isolation included.
/// </para>
/// </remarks>
public sealed class NotSpecification<T> : Specification<T>
{
    /// <summary>Initializes a new specification that negates the criteria of <paramref name="spec"/>.</summary>
    /// <param name="spec">The operand to negate. Must not be <see langword="null"/>.</param>
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
