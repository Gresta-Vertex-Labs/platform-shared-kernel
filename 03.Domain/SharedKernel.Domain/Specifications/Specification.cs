using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Base class for a named query specification: a subclass declares its filter, eager loading and ordering in
/// its constructor through the protected builder methods.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <remarks>
/// <para>
/// <b>Named or inline.</b> Derive from this class for a query that expresses a reusable business concept;
/// build a one-off query inline with <see cref="Spec.For{T}"/>, which has the same capabilities as fluent
/// methods.
/// </para>
/// <para>
/// <b>Usage.</b> Call the builder methods only from the subclass constructor, so an instance never changes
/// after it is created.
/// </para>
/// <para>
/// <b>Criteria.</b> <see cref="AddCriteria"/> accumulates: each call is combined with the existing criteria
/// by logical AND, so a subclass narrows an inherited filter and never replaces it.
/// </para>
/// <para>
/// <b>Eager loading.</b> <see cref="AddInclude{TProperty}"/> returns a builder whose <c>ThenInclude</c> continues
/// the path with full type checking, including through collections:
/// <c>AddInclude(o =&gt; o.Lines).ThenInclude(l =&gt; l.Product)</c>.
/// </para>
/// <para>
/// <b>Ordering.</b> A specification has exactly one primary sort, set by <see cref="ApplyOrderBy"/> or
/// <see cref="ApplyOrderByDescending"/>; a second call throws. Add further keys with
/// <see cref="ApplyThenBy"/>, which throws when no primary sort is set yet.
/// </para>
/// <para>
/// <b>Paging.</b> Page at the call site, by passing a page request to the repository. Use
/// <see cref="ApplyTake"/> / <see cref="ApplyPaging"/> only for a fixed window such as "the ten most recent".
/// </para>
/// <para>
/// <b>Composition.</b> Combine specifications with <see cref="SpecificationExtensions.And{T}"/>,
/// <see cref="SpecificationExtensions.Or{T}"/> and <see cref="SpecificationExtensions.Not{T}"/>; see
/// <see cref="SpecificationExtensions"/> for how ordering and paging are merged.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class ActiveOrdersSpec : Specification&lt;Order&gt;
/// {
///     public ActiveOrdersSpec(Guid customerId)
///     {
///         AddCriteria(o =&gt; o.CustomerId == customerId);
///         AddInclude(o =&gt; o.Lines).ThenInclude(l =&gt; l.Product);
///         ApplyOrderByDescending(o =&gt; o.CreatedOn);
///         ApplyThenByDescending(o =&gt; o.Id);
///     }
/// }
/// </code>
/// </example>
public abstract class Specification<T> : ISpecification<T>
{
    private readonly List<Expression<Func<T, object>>> _includes = [];
    private readonly List<(Expression<Func<T, object>> KeySelector, bool Descending)> _thenBys = [];
    private readonly List<string> _stringIncludes = [];
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
    public bool AsSplitQuery { get; private set; }

    /// <inheritdoc/>
    public bool IncludeDeleted { get; private set; }

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
    protected void AddCriteria(Expression<Func<T, bool>> criteria) => AddCriteriaCore(criteria);

    /// <summary>
    /// Adds a navigation to eager-load with the query and returns a builder whose <c>ThenInclude</c>
    /// continues the path.
    /// </summary>
    /// <typeparam name="TProperty">The navigation's type: an entity or a collection of entities.</typeparam>
    /// <param name="include">
    /// The navigation selector, such as <c>o =&gt; o.Lines</c>, or a filtered include such as
    /// <c>o =&gt; o.Lines.Where(l =&gt; l.IsActive)</c>. Must not be <see langword="null"/>.
    /// </param>
    /// <returns>A builder for continuing the include path with <c>ThenInclude</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="include"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A filtered include cannot be continued with <c>ThenInclude</c>; that call throws
    /// <see cref="NotSupportedException"/>.
    /// </remarks>
    protected IncludableSpecificationBuilder<T, TProperty> AddInclude<TProperty>(
        Expression<Func<T, TProperty>> include) =>
        AddIncludeCore(include);

    /// <summary>Sets the ascending primary sort.</summary>
    /// <param name="orderBy">The sort key selector. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="orderBy"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A primary sort, ascending or descending, is already set; add further keys with
    /// <see cref="ApplyThenBy"/>.
    /// </exception>
    protected void ApplyOrderBy(Expression<Func<T, object>> orderBy) => SetOrderCore(orderBy, descending: false);

    /// <summary>Sets the descending primary sort.</summary>
    /// <param name="orderByDescending">The sort key selector. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="orderByDescending"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A primary sort, ascending or descending, is already set; add further keys with
    /// <see cref="ApplyThenBy"/>.
    /// </exception>
    protected void ApplyOrderByDescending(Expression<Func<T, object>> orderByDescending) =>
        SetOrderCore(orderByDescending, descending: true);

    /// <summary>
    /// Adds an ascending secondary sort key, applied after the primary sort and any earlier secondary keys — the
    /// counterpart of <see cref="ApplyOrderBy"/>, as <see cref="ApplyThenByDescending"/> is of
    /// <see cref="ApplyOrderByDescending"/>.
    /// </summary>
    /// <param name="keySelector">The sort key selector. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="keySelector"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No primary sort is set yet.</exception>
    protected void ApplyThenBy(Expression<Func<T, object>> keySelector) =>
        AddThenByCore(keySelector, descending: false);

    /// <summary>
    /// Adds a descending secondary sort key, applied after the primary sort and any earlier secondary keys.
    /// </summary>
    /// <param name="keySelector">The sort key selector. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="keySelector"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No primary sort is set yet.</exception>
    protected void ApplyThenByDescending(Expression<Func<T, object>> keySelector) =>
        AddThenByCore(keySelector, descending: true);

    /// <summary>
    /// Sets a fixed row window: skips <paramref name="skip"/> ordered rows, then takes at most
    /// <paramref name="take"/>.
    /// </summary>
    /// <param name="skip">The number of rows to skip. Must be zero or greater.</param>
    /// <param name="take">The maximum number of rows to return. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="skip"/> is negative, or <paramref name="take"/> is less than 1.
    /// </exception>
    /// <remarks>
    /// For page-by-page access pass a page request to the repository instead. A primary sort is required when
    /// the query runs.
    /// </remarks>
    protected void ApplyPaging(int skip, int take)
    {
        SetSkipCore(skip);
        SetTakeCore(take);
    }

    /// <summary>Limits the query to at most <paramref name="take"/> ordered rows.</summary>
    /// <param name="take">The maximum number of rows to return. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="take"/> is less than 1.</exception>
    /// <remarks>A primary sort is required when the query runs.</remarks>
    protected void ApplyTake(int take) => SetTakeCore(take);

    /// <summary>Marks the query as distinct, so the evaluator removes duplicate rows.</summary>
    protected void ApplyDistinct() => IsDistinct = true;

    /// <summary>
    /// Marks the query to load its includes with one query per included collection instead of a single joined
    /// query.
    /// </summary>
    /// <remarks>
    /// Call it when the specification includes two or more collections; a single joined query returns their
    /// Cartesian product and repeats rows.
    /// </remarks>
    protected void ApplySplitQuery() => AsSplitQuery = true;

    /// <summary>Marks the query to return soft-deleted entities as well.</summary>
    /// <remarks>
    /// Only the soft-delete filter is bypassed; tenant isolation stays in force. Use it for admin, audit,
    /// export and recovery specifications, never for user-facing queries.
    /// </remarks>
    protected void IncludeSoftDeleted() => IncludeDeleted = true;

    /// <summary>
    /// Adds a dot-separated navigation path to eager-load, for deep paths such as
    /// <c>"Lines.Product.Supplier"</c>.
    /// </summary>
    /// <param name="path">The navigation path. Must not be <see langword="null"/>, empty or whitespace.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <remarks>
    /// Prefer <see cref="AddInclude{TProperty}"/> with <c>ThenInclude</c>, which the compiler checks. A
    /// misspelled string path fails only when the query runs.
    /// </remarks>
    protected void AddStringInclude(string path) => AddStringIncludeCore(path);

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
    /// <see cref="Criteria"/> into the database query. Only <see cref="Criteria"/> is evaluated.
    /// </para>
    /// <para>
    /// <b>Performance.</b> <see cref="Criteria"/> is compiled to a delegate on the first call and reused;
    /// under a race it may be compiled more than once. Criteria that call database-only functions throw here.
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
    /// <remarks>For anything beyond a filter, use <see cref="Spec.For{T}"/>.</remarks>
    public static Specification<T> Create(Expression<Func<T, bool>> criteria) =>
        new CriteriaSpecification<T>(criteria);

    // ---------------------------------------------------------------------------------------------------
    // Core mutators shared by the protected builder methods, the inline builder and the composites.
    // ---------------------------------------------------------------------------------------------------

    internal void AddCriteriaCore(Expression<Func<T, bool>> criteria)
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

    internal IncludableSpecificationBuilder<T, TProperty> AddIncludeCore<TProperty>(
        Expression<Func<T, TProperty>> include)
    {
        ArgumentNullException.ThrowIfNull(include);
        AddIncludeExpression(SpecificationExpressions.ToObjectSelector(include));
        return new IncludableSpecificationBuilder<T, TProperty>(this, SpecificationExpressions.TryGetMemberPath(include));
    }

    internal void AddIncludeExpression(Expression<Func<T, object>> include)
    {
        if (!_includes.Contains(include))
            _includes.Add(include);
    }

    internal void AddStringIncludeCore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!_stringIncludes.Contains(path, StringComparer.Ordinal))
            _stringIncludes.Add(path);
    }

    internal void SetOrderCore(Expression<Func<T, object>> keySelector, bool descending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);

        if (OrderBy is not null || OrderByDescending is not null)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} already has a primary sort. A specification has one primary sort; "
                + "add further sort keys with ThenBy.");
        }

        if (descending)
            OrderByDescending = keySelector;
        else
            OrderBy = keySelector;
    }

    internal void AddThenByCore(Expression<Func<T, object>> keySelector, bool descending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);

        if (OrderBy is null && OrderByDescending is null)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} has no primary sort. Set one with OrderBy/OrderByDescending before adding "
                + "a secondary sort key with ThenBy.");
        }

        _thenBys.Add((keySelector, descending));
    }

    internal void SetSkipCore(int skip)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        Skip = skip;
    }

    internal void SetTakeCore(int take)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        Take = take;
    }

    internal void SetDistinctCore() => IsDistinct = true;

    internal void SetSplitQueryCore() => AsSplitQuery = true;

    internal void SetIncludeDeletedCore() => IncludeDeleted = true;
}
