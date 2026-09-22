namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A specification matched only by entities that satisfy both of its operands' criteria.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <remarks>
/// <para>
/// <b>Criteria.</b> The left criteria AND the right criteria. An operand without criteria contributes no
/// condition; when neither operand has criteria, neither does the result.
/// </para>
/// <para>
/// <b>Query shape.</b> Includes and string includes of both operands are combined (duplicates once), and
/// Distinct, split-query and include-deleted are set when either operand sets them. The ordering of the one
/// operand that declares a primary sort is carried over.
/// </para>
/// <para>
/// <b>Throws instead of dropping.</b> Construction throws <see cref="InvalidOperationException"/> when both
/// operands declare a primary sort or either declares Skip/Take; page at the call site instead.
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
    /// <exception cref="InvalidOperationException">
    /// Both operands declare a primary sort, or either declares Skip/Take.
    /// </exception>
    public AndSpecification(ISpecification<T> left, ISpecification<T> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Criteria is not null)
            AddCriteriaCore(left.Criteria);
        if (right.Criteria is not null)
            AddCriteriaCore(right.Criteria);

        SpecificationComposition.MergeShape(this, "And", left, right);
    }
}
