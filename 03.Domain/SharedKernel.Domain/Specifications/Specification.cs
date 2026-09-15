using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Base class for a named query specification: a subclass declares its filter, eager loading, ordering and
/// paging in its constructor through the protected builder methods.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <remarks>
/// <para>
/// <b>Usage.</b> Call the builder methods only from the subclass constructor, so an instance never changes
/// after it is created. The builders return <see langword="void"/>; there is no fluent chaining.
/// </para>
/// <para>
/// <b>Criteria.</b> <see cref="AddCriteria"/> accumulates: each call is combined with the existing criteria
/// by logical AND, so a subclass narrows an inherited filter and never replaces it.
/// </para>
/// <para>
/// <b>Ordering.</b> A specification has exactly one primary sort, set by <see cref="ApplyOrderBy"/> or
/// <see cref="ApplyOrderByDescending"/>; a second call throws. Add further keys with
/// <see cref="ApplyThenBy"/>.
/// </para>
/// <para>
/// <b>Composition.</b> Combine specifications with <see cref="SpecificationExtensions.And{T}"/>,
/// <see cref="SpecificationExtensions.Or{T}"/> and <see cref="SpecificationExtensions.Not{T}"/>. The result
/// keeps criteria, includes and the tracking, split-query and include-deleted flags, but drops ordering,
/// paging and <see cref="IsDistinct"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class ActiveOrdersSpec : Specification&lt;Order&gt;
/// {
///     public ActiveOrdersSpec(Guid tenantId)
///     {
///         AddCriteria(o =&gt; !o.IsDeleted);
///         AddCriteria(o =&gt; o.TenantId == tenantId);
///         AddInclude(o =&gt; o.Lines);
///         ApplyOrderByDescending(o =&gt; o.CreatedOn);
///         ApplyThenBy(o =&gt; o.Id, descending: true);
///         ApplyPaging(skip: 0, take: 20);
///     }
/// }
/// </code>
/// </example>
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
    /// Adds a filter condition, combined by logical AND with the criteria already present.
    /// </summary>
    /// <param name="criteria">
    /// The condition an entity must also satisfy. Must not be <see langword="null"/>. Its lambda parameter is
    /// rebound onto the existing criteria's parameter, so parameter names need not match.
    /// </param>
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

    /// <summary>Adds a navigation to eager-load with the query.</summary>
    /// <param name="include">
    /// The navigation selector, such as <c>o =&gt; o.Lines</c>. Must not be <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="include"/> is <see langword="null"/>.</exception>
    protected void AddInclude(Expression<Func<T, object>> include)
    {
        ArgumentNullException.ThrowIfNull(include);
        _includes.Add(include);
    }

    /// <summary>Sets the ascending primary sort.</summary>
    /// <param name="orderBy">The sort key selector. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="orderBy"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A primary sort, ascending or descending, is already set; add further keys with
    /// <see cref="ApplyThenBy"/>.
    /// </exception>
    protected void ApplyOrderBy(Expression<Func<T, object>> orderBy)
    {
        ArgumentNullException.ThrowIfNull(orderBy);
        EnsureNoPrimarySort();
        OrderBy = orderBy;
    }

    /// <summary>Sets the descending primary sort.</summary>
    /// <param name="orderByDescending">The sort key selector. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="orderByDescending"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A primary sort, ascending or descending, is already set; add further keys with
    /// <see cref="ApplyThenBy"/>.
    /// </exception>
    protected void ApplyOrderByDescending(Expression<Func<T, object>> orderByDescending)
    {
        ArgumentNullException.ThrowIfNull(orderByDescending);
        EnsureNoPrimarySort();
        OrderByDescending = orderByDescending;
    }

    /// <summary>
    /// Copies <paramref name="source"/>'s includes and string includes and its tracking, split-query and
    /// include-deleted flags into this specification, for the composite specifications.
    /// </summary>
    /// <remarks>
    /// An include expression already present by reference is not added again; string paths are compared
    /// ordinally. Flags are combined with OR. Criteria, ordering, paging and Distinct are not copied.
    /// </remarks>
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

    /// <summary>Adds a secondary sort key, applied after the primary sort and any earlier secondary keys.</summary>
    /// <param name="keySelector">The sort key selector. Must not be <see langword="null"/>.</param>
    /// <param name="descending">
    /// <see langword="true"/> to sort this key in descending order; <see langword="false"/> for ascending.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="keySelector"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Secondary keys take effect only together with a primary sort; an evaluator ignores them otherwise.
    /// </remarks>
    protected void ApplyThenBy(Expression<Func<T, object>> keySelector, bool descending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        _thenBys.Add((keySelector, descending));
    }

    /// <summary>
    /// Adds a descending secondary sort key; equivalent to <c>ApplyThenBy(keySelector, descending: true)</c>.
    /// </summary>
    /// <param name="keySelector">The sort key selector. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="keySelector"/> is <see langword="null"/>.</exception>
    protected void ApplyThenByDescending(Expression<Func<T, object>> keySelector) =>
        ApplyThenBy(keySelector, descending: true);

    /// <summary>
    /// Sets offset paging: skips <paramref name="skip"/> ordered rows, then takes at most <paramref name="take"/>.
    /// </summary>
    /// <param name="skip">The number of rows to skip. Must be zero or greater.</param>
    /// <param name="take">The maximum number of rows to return. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="skip"/> is negative, or <paramref name="take"/> is less than 1.
    /// </exception>
    /// <remarks>
    /// A later call replaces the earlier values. Apply a primary sort as well; paging an unordered query
    /// returns rows in no guaranteed order. For a 1-based page number, derive from
    /// <see cref="PagedSpecification{T}"/> instead.
    /// </remarks>
    protected void ApplyPaging(int skip, int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        Skip = skip;
        Take = take;
    }

    /// <summary>Marks the query as distinct, so the evaluator removes duplicate rows.</summary>
    protected void ApplyDistinct() => IsDistinct = true;

    /// <summary>Marks the query as read-only, so the evaluator runs it without change tracking.</summary>
    /// <remarks>
    /// Never use it for a specification that loads entities to modify: changes to untracked entities are not
    /// saved.
    /// </remarks>
    protected void ApplyNoTracking() => _asNoTracking = true;

    /// <summary>
    /// Marks the query to load its includes with one query per included collection instead of a single joined
    /// query.
    /// </summary>
    /// <remarks>
    /// Call it when the specification includes two or more collections; a single joined query returns their
    /// Cartesian product and repeats rows.
    /// </remarks>
    protected void ApplySplitQuery() => _asSplitQuery = true;

    /// <summary>
    /// Marks the query to bypass the global query filters, so soft-deleted entities are returned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Usage.</b> Call it only for admin, audit, export and recovery specifications, never for user-facing
    /// queries.
    /// </para>
    /// <para>
    /// <b>Pitfall.</b> The bypass disables <em>every</em> global query filter, including tenant isolation. Add
    /// the tenant condition back with <see cref="AddCriteria"/> in the same constructor.
    /// </para>
    /// </remarks>
    protected void IncludeSoftDeleted() => _includeDeleted = true;

    /// <summary>
    /// Adds a dot-separated navigation path to eager-load, for deep paths such as
    /// <c>"Lines.Product.Supplier"</c>.
    /// </summary>
    /// <param name="path">The navigation path. Must not be <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <remarks>
    /// The path is not checked against the model here; a misspelled navigation fails when the query runs.
    /// </remarks>
    protected void AddStringInclude(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _stringIncludes.Add(path);
    }

    /// <summary>
    /// Returns whether <paramref name="entity"/> satisfies <see cref="Criteria"/>, evaluated in memory.
    /// </summary>
    /// <param name="entity">The entity to test.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="entity"/> satisfies <see cref="Criteria"/> or
    /// <see cref="Criteria"/> is <see langword="null"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Usage.</b> For in-domain checks and unit tests. Query evaluators never call it; they translate
    /// <see cref="Criteria"/> into the database query. Only <see cref="Criteria"/> is evaluated: includes,
    /// ordering, paging and <see cref="IncludeDeleted"/> play no part.
    /// </para>
    /// <para>
    /// <b>Performance.</b> <see cref="Criteria"/> is compiled to a delegate on the first call and the delegate
    /// is reused afterwards.
    /// </para>
    /// <para>
    /// <b>Thread safety.</b> Concurrent calls are safe; under a race the criteria may be compiled more than
    /// once.
    /// </para>
    /// <para>
    /// <b>Pitfall.</b> Criteria that call database-only functions cannot run in memory and throw here.
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
    /// Creates an unnamed specification that carries only <paramref name="criteria"/>, for a one-off filter.
    /// </summary>
    /// <param name="criteria">The condition an entity must satisfy. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A specification whose <see cref="Criteria"/> is <paramref name="criteria"/>, with no includes, ordering
    /// or paging and every flag <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="criteria"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The result composes with <see cref="SpecificationExtensions.And{T}"/>,
    /// <see cref="SpecificationExtensions.Or{T}"/> and <see cref="SpecificationExtensions.Not{T}"/>. Give a
    /// filter that expresses a reusable business concept its own named subclass instead.
    /// </remarks>
    /// <example>
    /// <code>
    /// var spec = new ActiveOrdersSpec(tenantId)
    ///     .And(Specification&lt;Order&gt;.Create(o =&gt; o.Total.Amount &gt; 100));
    /// </code>
    /// </example>
    public static Specification<T> Create(Expression<Func<T, bool>> criteria) =>
        new CriteriaSpecification<T>(criteria);
}
