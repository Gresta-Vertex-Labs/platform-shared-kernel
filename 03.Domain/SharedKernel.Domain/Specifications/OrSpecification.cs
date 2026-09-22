using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A specification matched by entities that satisfy the criteria of at least one of its operands.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <remarks>
/// <para>
/// <b>Criteria.</b> The left criteria OR the right criteria. An operand without criteria matches every
/// entity, so when either operand has no criteria the result has none and matches every entity.
/// </para>
/// <para>
/// <b>Query shape.</b> Merged exactly as in <see cref="AndSpecification{T}"/>: includes and flags combined,
/// the single declared ordering carried over, and construction throws when both operands declare a primary
/// sort or either declares Skip/Take.
/// </para>
/// </remarks>
public sealed class OrSpecification<T> : Specification<T>
{
    /// <summary>
    /// Initializes a new specification that combines <paramref name="left"/> and <paramref name="right"/> with
    /// logical OR.
    /// </summary>
    /// <param name="left">The first operand. Must not be <see langword="null"/>.</param>
    /// <param name="right">The second operand. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Both operands declare a primary sort, or either declares Skip/Take.
    /// </exception>
    public OrSpecification(ISpecification<T> left, ISpecification<T> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Criteria is not null && right.Criteria is not null)
        {
            var parameter = left.Criteria.Parameters[0];
            var rightBody = new ParameterReplacer(right.Criteria.Parameters[0], parameter).Visit(right.Criteria.Body);
            AddCriteriaCore(Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Criteria.Body, rightBody), parameter));
        }

        SpecificationComposition.MergeShape(this, "Or", left, right);
    }
}
