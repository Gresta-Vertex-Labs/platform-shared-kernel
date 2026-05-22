using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Abstract base class for query specifications. Subclasses build the specification in their
/// constructor using the protected builder methods.
/// </summary>
/// <typeparam name="T">The type of domain entity this specification applies to.</typeparam>
/// <remarks>
/// All builder methods are <c>protected void</c> and must be called exclusively from the
/// subclass constructor. No fluent chaining is permitted outside the constructor.
/// <example>
/// <code>
/// public sealed class ActiveOrdersSpec : Specification&lt;Order&gt;
/// {
///     public ActiveOrdersSpec()
///     {
///         AddCriteria(o => !o.IsDeleted);
///         ApplyOrderByDescending(o => o.CreatedOn);
///         ApplyPaging(skip: 0, take: 20);
///     }
/// }
/// </code>
/// </example>
/// </remarks>
public abstract class Specification<T> : ISpecification<T>
{
    private readonly List<Expression<Func<T, object>>> _includes = [];
    private readonly List<(Expression<Func<T, object>> KeySelector, bool Descending)> _thenBys = [];

    /// <inheritdoc/>
    public Expression<Func<T, bool>>? Criteria { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<Expression<Func<T, object>>> Includes => _includes.AsReadOnly();

    /// <inheritdoc/>
    public Expression<Func<T, object>>? OrderBy { get; private set; }

    /// <inheritdoc/>
    public Expression<Func<T, object>>? OrderByDescending { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<(Expression<Func<T, object>> KeySelector, bool Descending)> ThenBys =>
        _thenBys.AsReadOnly();

    /// <inheritdoc/>
    public int? Skip { get; private set; }

    /// <inheritdoc/>
    public int? Take { get; private set; }

    /// <inheritdoc/>
    public bool IsDistinct { get; private set; }

    /// <summary>Sets the filter predicate for this specification.</summary>
    protected void AddCriteria(Expression<Func<T, bool>> criteria) => Criteria = criteria;

    /// <summary>Adds a navigation property include for eager loading.</summary>
    protected void AddInclude(Expression<Func<T, object>> include) => _includes.Add(include);

    /// <summary>Sets the primary ascending sort expression.</summary>
    protected void ApplyOrderBy(Expression<Func<T, object>> orderBy) => OrderBy = orderBy;

    /// <summary>Sets the primary descending sort expression.</summary>
    protected void ApplyOrderByDescending(Expression<Func<T, object>> orderByDescending) =>
        OrderByDescending = orderByDescending;

    /// <summary>Adds a secondary sort expression after the primary sort.</summary>
    /// <param name="keySelector">The sort key selector.</param>
    /// <param name="descending"><see langword="true"/> for descending order; <see langword="false"/> for ascending.</param>
    protected void ApplyThenBy(Expression<Func<T, object>> keySelector, bool descending) =>
        _thenBys.Add((keySelector, descending));

    /// <summary>Applies paging by setting <see cref="Skip"/> and <see cref="Take"/>.</summary>
    protected void ApplyPaging(int skip, int take)
    {
        Skip = skip;
        Take = take;
    }

    /// <summary>Marks the specification as distinct — duplicate results will be eliminated.</summary>
    protected void ApplyDistinct() => IsDistinct = true;
}
