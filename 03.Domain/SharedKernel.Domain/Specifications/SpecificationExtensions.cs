namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Extension methods that compose specifications with logical AND, OR and NOT.
/// </summary>
/// <remarks>
/// <para>
/// <b>Composition.</b> Each result combines the operands' criteria and copies their includes and their
/// tracking, split-query and include-deleted flags. Operands are not modified.
/// </para>
/// <para>
/// <b>Pitfall.</b> Ordering, paging and Distinct are never copied, so compose filters first and put ordering
/// and paging in a named specification. Composing a <see cref="KeysetSpecification{T, TKey}"/> or
/// <see cref="PagedSpecification{T}"/> loses its paging.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var spec = new ActiveOrdersSpec(tenantId)
///     .And(Specification&lt;Order&gt;.Create(o =&gt; o.Total.Amount &gt; 100))
///     .And(Specification&lt;Order&gt;.Create(o =&gt; o.CustomerId == customerId).Not());
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
    /// <returns>A new <see cref="AndSpecification{T}"/> without ordering, paging or Distinct.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.
    /// </exception>
    public static AndSpecification<T> And<T>(this Specification<T> left, Specification<T> right) =>
        new(left, right);

    /// <summary>
    /// Returns a specification matched by entities that satisfy <paramref name="left"/>,
    /// <paramref name="right"/>, or both.
    /// </summary>
    /// <typeparam name="T">The entity type the query returns.</typeparam>
    /// <param name="left">The first operand. Must not be <see langword="null"/>.</param>
    /// <param name="right">The second operand. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A new <see cref="OrSpecification{T}"/> without ordering, paging or Distinct; it has no criteria, and
    /// matches every entity, when either operand has none.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.
    /// </exception>
    public static OrSpecification<T> Or<T>(this Specification<T> left, Specification<T> right) =>
        new(left, right);

    /// <summary>
    /// Returns a specification matched by entities that do not satisfy <paramref name="spec"/>'s criteria.
    /// </summary>
    /// <typeparam name="T">The entity type the query returns.</typeparam>
    /// <param name="spec">The operand to negate. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A new <see cref="NotSpecification{T}"/> without ordering, paging or Distinct; it matches nothing when
    /// <paramref name="spec"/> has no criteria.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <see langword="null"/>.</exception>
    public static NotSpecification<T> Not<T>(this Specification<T> spec) => new(spec);
}
