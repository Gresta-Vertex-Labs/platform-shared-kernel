namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A specification matched by no entity: the neutral starting point for building a filter with OR.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <remarks>
/// <para>
/// <b>Behaviour.</b> <see cref="Specification{T}.Criteria"/> is <c>_ =&gt; false</c>, so
/// <see cref="Specification{T}.IsSatisfiedBy"/> always returns <see langword="false"/> and a query returns
/// no rows. It has no includes, ordering or paging, and every flag is <see langword="false"/>.
/// </para>
/// <para>
/// <b>Composition.</b> OR with a specification that has criteria yields <c>false || criteria</c>, which
/// matches the same entities as that specification. OR with a specification without criteria (such as
/// <see cref="AllSpecification{T}"/>) yields no criteria, matching every entity. AND with anything matches
/// nothing.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Specification&lt;Order&gt; spec = new EmptySpecification&lt;Order&gt;();
/// foreach (var status in statuses)
///     spec = spec.Or(Specification&lt;Order&gt;.Create(o =&gt; o.Status == status));
/// </code>
/// </example>
public sealed class EmptySpecification<T> : Specification<T>
{
    /// <summary>Initializes a new specification whose criteria is <c>_ =&gt; false</c>.</summary>
    public EmptySpecification() => AddCriteria(_ => false);
}
