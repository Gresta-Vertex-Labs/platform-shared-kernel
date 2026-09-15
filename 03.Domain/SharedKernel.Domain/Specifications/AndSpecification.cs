namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A specification matched only by entities that satisfy both of its operands' criteria.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <remarks>
/// <para>
/// <b>Criteria.</b> The left criteria AND the right criteria. An operand without criteria contributes no
/// condition, so combining with <see cref="AllSpecification{T}"/> leaves the other operand's criteria
/// unchanged; when neither operand has criteria, neither does the result.
/// </para>
/// <para>
/// <b>Query shape.</b> Copies the includes and string includes of both operands, left first (an include
/// expression instance or an ordinally equal path already present is not added twice), and sets
/// <see cref="ISpecification{T}.AsNoTracking"/>, <see cref="ISpecification{T}.AsSplitQuery"/> and
/// <see cref="ISpecification{T}.IncludeDeleted"/> when either operand sets them.
/// </para>
/// <para>
/// <b>Pitfall.</b> Ordering, paging and <see cref="ISpecification{T}.IsDistinct"/> are never copied: two
/// operands can disagree about them and no merge is correct in general. The result is unordered and unpaged.
/// If either operand includes soft-deleted entities, the whole result bypasses the global query filters,
/// tenant isolation included.
/// </para>
/// </remarks>
public sealed class AndSpecification<T> : Specification<T>
{
    /// <summary>
    /// Initializes a new specification that combines <paramref name="left"/> and <paramref name="right"/> with
    /// logical AND.
    /// </summary>
    /// <param name="left">The first operand. Must not be <see langword="null"/>.</param>
    /// <param name="right">The second operand. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.
    /// </exception>
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
