namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A specification that matches no entity — the identity element for OR composition.
/// </summary>
/// <typeparam name="T">The type of domain entity this specification applies to.</typeparam>
/// <remarks>
/// <para>
/// <see cref="ISpecification{T}.Criteria"/> is set to <c>_ =&gt; false</c>, meaning no entity
/// satisfies this specification. <see cref="Specification{T}.IsSatisfiedBy"/> always returns
/// <see langword="false"/>.
/// </para>
/// <para>
/// <strong>Identity element semantics:</strong>
/// <c>Or(EmptySpecification, spec)</c> effectively returns <c>spec</c> because an
/// <see cref="OrSpecification{T}"/> with a null-criteria right operand (the empty spec has a
/// non-null criteria) adopts the left operand's criteria via expression combination, but
/// logically an <see cref="EmptySpecification{T}"/> combined via OR does not restrict results.
/// </para>
/// <para>
/// <strong>Note on OR with AllSpecification:</strong>
/// <c>Or(AllSpecification, spec)</c> produces null criteria (matches everything) because
/// <see cref="AllSpecification{T}"/> has null criteria and <see cref="OrSpecification{T}"/>
/// propagates null when either operand has null criteria.
/// </para>
/// <example>
/// <code>
/// // Compose a base "none" specification with a runtime filter:
/// ISpecification&lt;Order&gt; spec = new EmptySpecification&lt;Order&gt;().Or(new ActiveOrdersSpec());
/// // spec effectively matches active orders only
/// </code>
/// </example>
/// </remarks>
public sealed class EmptySpecification<T> : Specification<T>
{
    /// <summary>Initialises the empty specification with a criteria that always returns <see langword="false"/>.</summary>
    public EmptySpecification() => AddCriteria(_ => false);
}
