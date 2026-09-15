using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Abstract base class for query specifications. Subclasses build the specification in their
/// constructor using the protected builder methods.
/// </summary>
/// <typeparam name="T">The type of domain entity this specification applies to.</typeparam>
/// <remarks>
/// <para>
/// All builder methods are <c>protected void</c> and must be called exclusively from the
/// subclass constructor. No fluent chaining is permitted outside the constructor.
/// </para>
/// <para>
/// <see cref="AddCriteria"/> accumulates: each call is combined with the criteria already present by
/// logical AND, so a subclass can add a tenant or status condition to an inherited filter without
/// replacing it.
/// </para>
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
    private readonly List<string> _stringIncludes = [];
    private bool _asNoTracking;
    private bool _asSplitQuery;
    private bool _includeDeleted;
    private Func<T, bool>? _compiledCriteria;

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

    /// <inheritdoc/>
    public bool AsNoTracking => _asNoTracking;

    /// <inheritdoc/>
    public bool AsSplitQuery => _asSplitQuery;

    /// <inheritdoc/>
    public bool IncludeDeleted => _includeDeleted;

    /// <inheritdoc/>
    public IReadOnlyList<string> StringIncludes => _stringIncludes.AsReadOnly();

    /// <summary>
    /// Adds a filter condition, combined by logical AND with any criteria already present.
    /// </summary>
    /// <param name="criteria">The condition an entity must satisfy.</param>
    /// <exception cref="ArgumentNullException"><paramref name="criteria"/> is <see langword="null"/>.</exception>
    protected void AddCriteria(Expression<Func<T, bool>> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        if (Criteria is null)
        {
            Criteria = criteria;
        }
        else
        {
            var parameter = Criteria.Parameters[0];
            var body = new ParameterReplacer(criteria.Parameters[0], parameter).Visit(criteria.Body);
            Criteria = Expression.Lambda<Func<T, bool>>(Expression.AndAlso(Criteria.Body, body), parameter);
        }

        _compiledCriteria = null;
    }

    /// <summary>Adds a navigation property include for eager loading.</summary>
    /// <param name="include">The navigation to load.</param>
    /// <exception cref="ArgumentNullException"><paramref name="include"/> is <see langword="null"/>.</exception>
    protected void AddInclude(Expression<Func<T, object>> include)
    {
        ArgumentNullException.ThrowIfNull(include);
        _includes.Add(include);
    }

    /// <summary>Sets the primary ascending sort.</summary>
    /// <param name="orderBy">The sort key selector.</param>
    /// <exception cref="ArgumentNullException"><paramref name="orderBy"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A primary sort is already set; add further keys with <see cref="ApplyThenBy"/>.</exception>
    protected void ApplyOrderBy(Expression<Func<T, object>> orderBy)
    {
        ArgumentNullException.ThrowIfNull(orderBy);
        EnsureNoPrimarySort();
        OrderBy = orderBy;
    }

    /// <summary>Sets the primary descending sort.</summary>
    /// <param name="orderByDescending">The sort key selector.</param>
    /// <exception cref="ArgumentNullException"><paramref name="orderByDescending"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A primary sort is already set; add further keys with <see cref="ApplyThenBy"/>.</exception>
    protected void ApplyOrderByDescending(Expression<Func<T, object>> orderByDescending)
    {
        ArgumentNullException.ThrowIfNull(orderByDescending);
        EnsureNoPrimarySort();
        OrderByDescending = orderByDescending;
    }

    /// <summary>
    /// Carries <paramref name="source"/>'s includes (without duplicates) and its tracking, split-query and
    /// include-deleted flags into this specification. Used by the composite specifications.
    /// </summary>
    private protected void CopyQueryShapeFrom(Specification<T> source)
    {
        foreach (var include in source._includes)
        {
            if (!_includes.Contains(include))
                _includes.Add(include);
        }

        foreach (var path in source._stringIncludes)
        {
            if (!_stringIncludes.Contains(path, StringComparer.Ordinal))
                _stringIncludes.Add(path);
        }

        _asNoTracking |= source._asNoTracking;
        _asSplitQuery |= source._asSplitQuery;
        _includeDeleted |= source._includeDeleted;
    }

    private void EnsureNoPrimarySort()
    {
        if (OrderBy is not null || OrderByDescending is not null)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} already has a primary sort. A specification has one primary sort; "
                + $"add further sort keys with {nameof(ApplyThenBy)}.");
        }
    }

    /// <summary>Adds a secondary sort expression after the primary sort.</summary>
    /// <param name="keySelector">The sort key selector.</param>
    /// <param name="descending"><see langword="true"/> for descending order; <see langword="false"/> for ascending.</param>
    /// <exception cref="ArgumentNullException"><paramref name="keySelector"/> is <see langword="null"/>.</exception>
    protected void ApplyThenBy(Expression<Func<T, object>> keySelector, bool descending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        _thenBys.Add((keySelector, descending));
    }

    /// <summary>
    /// Adds a secondary descending sort expression after the primary sort.
    /// Alias for <c>ApplyThenBy(keySelector, descending: true)</c>.
    /// </summary>
    /// <param name="keySelector">The sort key selector.</param>
    protected void ApplyThenByDescending(Expression<Func<T, object>> keySelector) =>
        ApplyThenBy(keySelector, descending: true);

    /// <summary>Applies paging by setting <see cref="Skip"/> and <see cref="Take"/>.</summary>
    /// <param name="skip">The number of entities to skip. Must not be negative.</param>
    /// <param name="take">The maximum number of entities to return. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="skip"/> is negative, or <paramref name="take"/> is less than 1.</exception>
    protected void ApplyPaging(int skip, int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        Skip = skip;
        Take = take;
    }

    /// <summary>Marks the specification as distinct — duplicate results will be eliminated.</summary>
    protected void ApplyDistinct() => IsDistinct = true;

    /// <summary>
    /// Marks this specification as no-tracking — the consuming repository must suppress
    /// change-tracking (e.g., <c>AsNoTracking()</c>) when applying this specification.
    /// </summary>
    protected void ApplyNoTracking() => _asNoTracking = true;

    /// <summary>
    /// Marks this specification as requiring EF Core's split-query execution (<c>AsSplitQuery()</c>)
    /// instead of a single Cartesian-joined query.
    /// </summary>
    /// <remarks>
    /// Call this when the specification declares two or more collection <see cref="AddInclude"/>
    /// entries, to avoid duplicated rows in the result set from the Cartesian product a single
    /// joined query would otherwise produce.
    /// </remarks>
    protected void ApplySplitQuery() => _asSplitQuery = true;

    /// <summary>
    /// Marks this specification as soft-delete-inclusive — the consuming repository must bypass the
    /// global soft-delete query filter so that soft-deleted records appear in results.
    /// </summary>
    /// <remarks>
    /// Call this only in constructors of admin, audit, export, or recovery specifications.
    /// Never call it from read-model or user-facing query specifications.
    /// See <see cref="ISpecification{T}.IncludeDeleted"/> for the full bypass warning regarding
    /// tenant isolation.
    /// </remarks>
    protected void IncludeSoftDeleted() => _includeDeleted = true;

    /// <summary>
    /// Adds a string-based navigation-include path for deep eager loading.
    /// </summary>
    /// <param name="path">
    /// A dot-separated navigation path (e.g., <c>"Orders.Items.Product"</c>).
    /// Must not be <see langword="null"/> or whitespace.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or whitespace.</exception>
    protected void AddStringInclude(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _stringIncludes.Add(path);
    }

    /// <summary>
    /// Evaluates whether <paramref name="entity"/> satisfies this specification's <see cref="Criteria"/>.
    /// Intended for in-domain validation and unit-test assertions only — repository implementations
    /// in <c>06.Persistence</c> must not call this method.
    /// </summary>
    /// <param name="entity">The entity to evaluate.</param>
    /// <returns>
    /// <see langword="true"/> if the entity satisfies the criteria, or if <see cref="Criteria"/> is
    /// <see langword="null"/> (a criteria-less specification matches all entities).
    /// </returns>
    /// <remarks>
    /// <para>
    /// The <see cref="Criteria"/> expression is compiled to a <see cref="Func{T, TResult}"/> delegate
    /// exactly once and cached in a private field on the first call. Subsequent calls reuse the cached
    /// delegate with no recompilation overhead. Reusing a specification instance across calls is safe.
    /// </para>
    /// <para>
    /// When <see cref="Criteria"/> is <see langword="null"/>, the method returns <see langword="true"/>
    /// unconditionally — a criteria-less specification is interpreted as "match all".
    /// </para>
    /// </remarks>
    public bool IsSatisfiedBy(T entity)
    {
        if (Criteria is null)
            return true;

        _compiledCriteria ??= Criteria.Compile();
        return _compiledCriteria(entity);
    }

    /// <summary>
    /// Creates an ad hoc, criteria-only specification for a genuinely one-off/throwaway filter.
    /// </summary>
    /// <param name="criteria">The filter predicate.</param>
    /// <returns>
    /// A <see cref="Specification{T}"/> wrapping <paramref name="criteria"/> with no includes,
    /// ordering, or paging; <c>AsNoTracking</c>/<c>IncludeDeleted</c>/<c>AsSplitQuery</c> all
    /// default <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="criteria"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Returns <see cref="Specification{T}"/> so the result composes with <c>And</c>, <c>Or</c> and
    /// <c>Not</c>. A reusable business concept still deserves its own named subclass; use this only for
    /// a genuinely single-use filter.
    /// </remarks>
    public static Specification<T> Create(Expression<Func<T, bool>> criteria) =>
        new CriteriaSpecification<T>(criteria);
}
