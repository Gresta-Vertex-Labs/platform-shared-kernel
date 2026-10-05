namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A specification with no criteria, matched by every entity: the neutral starting point for building a
/// filter with AND.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <remarks>
/// <para>
/// <b>Behaviour.</b> <see cref="Specification{T}.Criteria"/> is <see langword="null"/>, so
/// <see cref="Specification{T}.IsSatisfiedBy"/> always returns <see langword="true"/>. It has no includes,
/// ordering or paging, and every flag is <see langword="false"/>.
/// </para>
/// <para>
/// <b>Composition.</b> AND with another specification yields that specification's criteria and query shape;
/// OR with any specification yields no criteria, matching every entity; NOT yields a specification that
/// matches nothing.
/// </para>
/// <para>
/// <b>Pitfall.</b> AND does not return the other operand itself; its ordering is carried over, and an
/// operand with Skip/Take makes the composition throw.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Specification&lt;Order&gt; spec = new AllSpecification&lt;Order&gt;();
/// if (customerId is not null)
///     spec = spec.And(Specification&lt;Order&gt;.Create(o =&gt; o.CustomerId == customerId));
/// </code>
/// </example>
public sealed class AllSpecification<T> : Specification<T>
{
    // No criteria, no ordering, no paging — matches everything.
}
