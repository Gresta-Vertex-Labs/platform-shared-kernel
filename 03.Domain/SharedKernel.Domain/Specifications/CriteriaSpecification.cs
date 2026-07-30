using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Ad hoc, criteria-only specification backing <see cref="Specification{T}.Create"/> — a third
/// sealed-wrapper sentinel alongside <see cref="AllSpecification{T}"/>/<see cref="EmptySpecification{T}"/>.
/// </summary>
/// <typeparam name="T">The type of domain entity this specification applies to.</typeparam>
/// <remarks>
/// Internal — never referenced directly by consumers. Constructed exclusively via
/// <see cref="Specification{T}.Create"/>.
/// </remarks>
internal sealed class CriteriaSpecification<T> : Specification<T>
{
    /// <summary>
    /// Initialises a new <see cref="CriteriaSpecification{T}"/> with the supplied
    /// <paramref name="criteria"/> and no other builder calls — no includes, no ordering, no
    /// paging; <c>AsNoTracking</c>/<c>IncludeDeleted</c>/<c>AsSplitQuery</c> all default <see langword="false"/>.
    /// </summary>
    public CriteriaSpecification(Expression<Func<T, bool>> criteria) => AddCriteria(criteria);
}
