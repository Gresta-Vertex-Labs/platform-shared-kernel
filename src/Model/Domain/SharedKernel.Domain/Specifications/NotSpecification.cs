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
/// <b>Query shape.</b> Copies the operand's includes, flags and ordering. Construction throws
/// <see cref="InvalidOperationException"/> when the operand declares Skip/Take. Negation applies to the
/// criteria only: an operand that includes soft-deleted entities still does.
/// </para>
/// </remarks>
public sealed class NotSpecification<T> : Specification<T>
{
    /// <summary>Initializes a new specification that negates the criteria of <paramref name="spec"/>.</summary>
    /// <param name="spec">The operand to negate. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="spec"/> declares Skip/Take.</exception>
    public NotSpecification(ISpecification<T> spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Criteria is null)
        {
            AddCriteriaCore(_ => false);
        }
        else
        {
            var parameter = spec.Criteria.Parameters[0];
            AddCriteriaCore(Expression.Lambda<Func<T, bool>>(Expression.Not(spec.Criteria.Body), parameter));
        }

        SpecificationComposition.MergeShape(this, "Not", spec);
    }
}
