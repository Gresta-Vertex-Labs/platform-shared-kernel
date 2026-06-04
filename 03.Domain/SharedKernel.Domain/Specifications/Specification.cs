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
    private readonly List<string> _stringIncludes = [];
    private bool _asNoTracking;
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
    public bool IncludeDeleted => _includeDeleted;

    /// <inheritdoc/>
    public IReadOnlyList<string> StringIncludes => _stringIncludes.AsReadOnly();

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

    /// <summary>
    /// Adds a secondary descending sort expression after the primary sort.
    /// Alias for <c>ApplyThenBy(keySelector, descending: true)</c>.
    /// </summary>
    /// <param name="keySelector">The sort key selector.</param>
    protected void ApplyThenByDescending(Expression<Func<T, object>> keySelector) =>
        ApplyThenBy(keySelector, descending: true);

    /// <summary>Applies paging by setting <see cref="Skip"/> and <see cref="Take"/>.</summary>
    protected void ApplyPaging(int skip, int take)
    {
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
}
