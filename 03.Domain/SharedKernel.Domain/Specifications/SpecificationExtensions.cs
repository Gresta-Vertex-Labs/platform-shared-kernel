namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Extension methods that compose specifications with logical AND, OR and NOT.
/// </summary>
/// <remarks>
/// <para>
/// <b>Criteria.</b> Combined as the method names say. Operands are not modified.
/// </para>
/// <para>
/// <b>Query shape, never dropped silently.</b> Includes, string includes, Distinct, split-query and
/// include-deleted are combined (a flag is set when any operand sets it). Ordering is carried over from the
/// single operand that declares a primary sort. Composition <em>throws</em>
/// <see cref="InvalidOperationException"/> when two operands both declare a primary sort, or when any operand
/// declares Skip/Take, because no merge is correct in general: compose the filters first and page the result
/// at the call site.
/// </para>
/// <para>
/// <b>Pitfall.</b> If either operand includes soft-deleted entities, the whole result does.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var spec = new ActiveOrdersSpec(customerId)
///     .And(Specification&lt;Order&gt;.Create(o =&gt; o.Total.Amount &gt; 100))
///     .And(Specification&lt;Order&gt;.Create(o =&gt; o.IsFlagged).Not());
/// </code>
/// </example>
public static class SpecificationExtensions
{
    /// <summary>
    /// Returns a specification matched only by entities that satisfy both <paramref name="left"/> and
    /// <paramref name="right"/>.
    /// </summary>
    /// <typeparam name="T">The entity type the query returns.</typeparam>
    /// <param name="left">The first operand. Must not be <see langword="null"/>.</param>
    /// <param name="right">The second operand. Must not be <see langword="null"/>.</param>
    /// <returns>A new <see cref="AndSpecification{T}"/>.</returns>
    /// <exception cref="ArgumentNullException">An operand is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Both operands declare a primary sort, or either declares Skip/Take.
    /// </exception>
    public static AndSpecification<T> And<T>(this ISpecification<T> left, ISpecification<T> right) =>
        new(left, right);

    /// <summary>
    /// Returns a specification matched by entities that satisfy <paramref name="left"/>,
    /// <paramref name="right"/>, or both.
    /// </summary>
    /// <typeparam name="T">The entity type the query returns.</typeparam>
    /// <param name="left">The first operand. Must not be <see langword="null"/>.</param>
    /// <param name="right">The second operand. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A new <see cref="OrSpecification{T}"/>; it has no criteria, and matches every entity, when either operand
    /// has none.
    /// </returns>
    /// <exception cref="ArgumentNullException">An operand is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Both operands declare a primary sort, or either declares Skip/Take.
    /// </exception>
    public static OrSpecification<T> Or<T>(this ISpecification<T> left, ISpecification<T> right) =>
        new(left, right);

    /// <summary>
    /// Returns a specification matched by entities that do not satisfy <paramref name="spec"/>'s criteria.
    /// </summary>
    /// <typeparam name="T">The entity type the query returns.</typeparam>
    /// <param name="spec">The operand to negate. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A new <see cref="NotSpecification{T}"/>; it matches nothing when <paramref name="spec"/> has no criteria.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="spec"/> declares Skip/Take.</exception>
    public static NotSpecification<T> Not<T>(this ISpecification<T> spec) => new(spec);
}
