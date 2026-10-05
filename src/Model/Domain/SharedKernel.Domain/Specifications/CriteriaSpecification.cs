using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// An unnamed specification that carries only criteria; the implementation behind
/// <see cref="Specification{T}.Create"/>.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <remarks>
/// Create instances through <see cref="Specification{T}.Create"/>; callers see only
/// <see cref="Specification{T}"/>.
/// </remarks>
internal sealed class CriteriaSpecification<T> : Specification<T>
{
    /// <summary>
    /// Initializes a new specification whose only content is <paramref name="criteria"/>: no includes,
    /// ordering or paging, and every flag <see langword="false"/>.
    /// </summary>
    /// <param name="criteria">The condition an entity must satisfy. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="criteria"/> is <see langword="null"/>.</exception>
    public CriteriaSpecification(Expression<Func<T, bool>> criteria) => AddCriteria(criteria);
}
