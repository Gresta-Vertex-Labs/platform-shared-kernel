namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A specification that matches all entities — the identity element for AND composition.
/// </summary>
/// <typeparam name="T">The type of domain entity this specification applies to.</typeparam>
/// <remarks>
/// <para>
/// <see cref="ISpecification{T}.Criteria"/> is <see langword="null"/>, meaning every entity
/// satisfies this specification. <see cref="Specification{T}.IsSatisfiedBy"/> always returns
/// <see langword="true"/>.
/// </para>
/// <para>
/// <strong>Identity element semantics:</strong>
/// <c>And(AllSpecification, spec)</c> effectively returns <c>spec</c> because an
/// <see cref="AndSpecification{T}"/> with a null-criteria left operand adopts the right
/// operand's criteria. <see cref="AllSpecification{T}"/> is the AND identity element.
/// </para>
/// <example>
/// <code>
/// // Compose a base "all" specification with a runtime filter:
/// ISpecification&lt;Order&gt; spec = new AllSpecification&lt;Order&gt;().And(new ActiveOrdersSpec());
/// // spec.Criteria == ActiveOrdersSpec.Criteria (AllSpecification is transparent in AND)
/// </code>
/// </example>
/// </remarks>
public sealed class AllSpecification<T> : Specification<T>
{
    // No criteria, no ordering, no paging — matches everything.
}
