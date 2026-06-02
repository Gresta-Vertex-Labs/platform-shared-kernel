using System.Linq.Expressions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.Abstractions.Specifications;

/// <summary>
/// Extends <see cref="ISpecification{TAggregate}"/> with a projection selector that maps
/// each matched aggregate to a result type.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type this specification queries.</typeparam>
/// <typeparam name="TResult">The projection output type.</typeparam>
/// <remarks>
/// <para>
/// Implementations extend a concrete <c>Specification&lt;TAggregate&gt;</c> base and supply
/// the <see cref="Selector"/> expression. The expression tree is AOT-safe on <c>IQueryable</c>
/// as long as the lambda body contains no runtime reflection APIs.
/// </para>
/// <para>
/// <see cref="Selector"/> is applied by the persistence layer <strong>after</strong> all paging
/// steps (Skip/Take) to preserve the paging-last invariant. It is the final step in the
/// query pipeline.
/// </para>
/// <para>Zero ORM dependencies — this interface lives in Abstractions.</para>
/// </remarks>
public interface IProjectionSpecification<TAggregate, TResult> : ISpecification<TAggregate>
{
    /// <summary>
    /// Gets the expression that projects each <typeparamref name="TAggregate"/> instance to
    /// a <typeparamref name="TResult"/> value.
    /// </summary>
    /// <remarks>
    /// Expression tree — AOT-safe on <see cref="System.Linq.IQueryable{T}"/>.
    /// The selector is applied after the full aggregate pipeline (criteria, includes, ordering,
    /// paging) by the <c>SpecificationEvaluator</c>.
    /// </remarks>
    Expression<Func<TAggregate, TResult>> Selector { get; }
}
