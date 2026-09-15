using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A specification satisfied by entities that satisfy at least one operand.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
/// <remarks>
/// <para>
/// When either operand has no criteria it matches every entity, so the combination has no criteria either.
/// Includes, string includes and the tracking, split-query and include-deleted flags are carried over as in
/// <see cref="AndSpecification{T}"/>.
/// </para>
/// <para><b>Ordering, paging and <c>Distinct</c> are not carried over.</b></para>
/// </remarks>
public sealed class OrSpecification<T> : Specification<T>
{
    /// <summary>Combines <paramref name="left"/> and <paramref name="right"/> with logical OR.</summary>
    /// <param name="left">The first specification.</param>
    /// <param name="right">The second specification.</param>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.</exception>
    public OrSpecification(Specification<T> left, Specification<T> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Criteria is not null && right.Criteria is not null)
        {
            var parameter = left.Criteria.Parameters[0];
            var rightBody = new ParameterReplacer(right.Criteria.Parameters[0], parameter).Visit(right.Criteria.Body);
            AddCriteria(Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Criteria.Body, rightBody), parameter));
        }

        CopyQueryShapeFrom(left);
        CopyQueryShapeFrom(right);
    }
}
