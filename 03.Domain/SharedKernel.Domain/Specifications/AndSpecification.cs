namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A specification satisfied only by entities that satisfy both operands.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
/// <remarks>
/// <para>
/// Carries over the criteria (combined with AND), every include and string include (without duplicates),
/// and the <see cref="ISpecification{T}.AsNoTracking"/>, <see cref="ISpecification{T}.AsSplitQuery"/> and
/// <see cref="ISpecification{T}.IncludeDeleted"/> flags, each set when either operand sets it.
/// </para>
/// <para>
/// <b>Ordering, paging and <c>Distinct</c> are not carried over</b>: two operands can disagree about them,
/// and no merge is correct in general. Compose the filters, then apply ordering and paging in a named
/// specification.
/// </para>
/// </remarks>
public sealed class AndSpecification<T> : Specification<T>
{
    /// <summary>Combines <paramref name="left"/> and <paramref name="right"/> with logical AND.</summary>
    /// <param name="left">The first specification.</param>
    /// <param name="right">The second specification.</param>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.</exception>
    public AndSpecification(Specification<T> left, Specification<T> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Criteria is not null)
            AddCriteria(left.Criteria);
        if (right.Criteria is not null)
            AddCriteria(right.Criteria);

        CopyQueryShapeFrom(left);
        CopyQueryShapeFrom(right);
    }
}
