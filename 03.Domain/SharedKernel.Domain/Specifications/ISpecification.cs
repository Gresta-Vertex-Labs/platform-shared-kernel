using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A query over entities of type <typeparamref name="T"/>: the filter, eager loading, ordering, paging
/// and query-shape flags a persistence-layer query evaluator translates into one database query.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <remarks>
/// <para>
/// <b>Usage.</b> Derive from <see cref="Specification{T}"/> (or <see cref="ReadOnlySpecification{T}"/>,
/// <see cref="PagedSpecification{T}"/>, <see cref="KeysetSpecification{T, TKey}"/>) rather than implementing
/// this interface directly; the base class enforces the invariants below. The domain declares what to
/// fetch; the evaluator decides how.
/// </para>
/// <para>
/// <b>Evaluation contract.</b> An evaluator must, in this order:
/// <list type="number">
///   <item><description>Bypass every global query filter when <see cref="IncludeDeleted"/> is
///   <see langword="true"/>.</description></item>
///   <item><description>Filter by <see cref="Criteria"/>, when not <see langword="null"/>.</description></item>
///   <item><description>Eager-load <see cref="Includes"/>, then <see cref="StringIncludes"/>, and split the
///   query when <see cref="AsSplitQuery"/> is <see langword="true"/>.</description></item>
///   <item><description>Sort by the one primary sort (<see cref="OrderBy"/> or <see cref="OrderByDescending"/>),
///   then by each <see cref="ThenBys"/> key in list order. Without a primary sort, ignore
///   <see cref="ThenBys"/>.</description></item>
///   <item><description>Apply <see cref="IsDistinct"/> and <see cref="AsNoTracking"/>.</description></item>
///   <item><description>Apply <see cref="Skip"/> and then <see cref="Take"/> last, after ordering, so a page
///   is taken from a stable order.</description></item>
/// </list>
/// A <see cref="KeysetSpecification{T, TKey}"/> replaces the offset step with a seek predicate; see that
/// type.
/// </para>
/// <para>
/// <b>Composition.</b> <see cref="AndSpecification{T}"/>, <see cref="OrSpecification{T}"/> and
/// <see cref="NotSpecification{T}"/> combine criteria and copy includes and the
/// <see cref="AsNoTracking"/>, <see cref="AsSplitQuery"/> and <see cref="IncludeDeleted"/> flags (each set
/// when any operand sets it). They never copy ordering, paging or <see cref="IsDistinct"/>.
/// </para>
/// </remarks>
public interface ISpecification<T>
{
    /// <summary>
    /// Gets the filter predicate an entity must satisfy, or <see langword="null"/> when every entity matches.
    /// </summary>
    /// <remarks>
    /// <see cref="Specification{T}"/> builds this as the logical AND of every condition added to it, expressed
    /// over a single lambda parameter.
    /// </remarks>
    Expression<Func<T, bool>>? Criteria { get; }

    /// <summary>
    /// Gets the navigation selectors to eager-load, in the order they were added; empty when none.
    /// </summary>
    IReadOnlyList<Expression<Func<T, object>>> Includes { get; }

    /// <summary>
    /// Gets the ascending primary sort key, or <see langword="null"/> when the primary sort is descending or
    /// absent.
    /// </summary>
    /// <remarks>
    /// At most one of <see cref="OrderBy"/> and <see cref="OrderByDescending"/> is non-null.
    /// </remarks>
    Expression<Func<T, object>>? OrderBy { get; }

    /// <summary>
    /// Gets the descending primary sort key, or <see langword="null"/> when the primary sort is ascending or
    /// absent.
    /// </summary>
    /// <remarks>
    /// At most one of <see cref="OrderBy"/> and <see cref="OrderByDescending"/> is non-null.
    /// </remarks>
    Expression<Func<T, object>>? OrderByDescending { get; }

    /// <summary>
    /// Gets the secondary sort keys, applied after the primary sort in list order; each entry pairs a key
    /// selector with <see langword="true"/> for descending order.
    /// </summary>
    /// <remarks>
    /// An evaluator ignores these keys when neither <see cref="OrderBy"/> nor <see cref="OrderByDescending"/>
    /// is set.
    /// </remarks>
    IReadOnlyList<(Expression<Func<T, object>> KeySelector, bool Descending)> ThenBys { get; }

    /// <summary>
    /// Gets the number of ordered rows to skip before taking a page, or <see langword="null"/> when the query
    /// is not paged.
    /// </summary>
    /// <remarks>
    /// <see cref="Specification{T}"/> sets <see cref="Skip"/> and <see cref="Take"/> together: when set,
    /// <see cref="Skip"/> is zero or greater and <see cref="Take"/> is at least 1.
    /// </remarks>
    int? Skip { get; }

    /// <summary>
    /// Gets the maximum number of rows to return, or <see langword="null"/> when the query is not paged.
    /// </summary>
    /// <remarks>
    /// When set through <see cref="Specification{T}"/>, the value is at least 1.
    /// </remarks>
    int? Take { get; }

    /// <summary>Gets a value indicating whether duplicate rows are removed from the result.</summary>
    bool IsDistinct { get; }

    /// <summary>
    /// Gets a value indicating whether the evaluator must run the query without change tracking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Default.</b> <see langword="false"/>, so entities loaded to be modified stay tracked. Set it for
    /// read-only queries; <see cref="ReadOnlySpecification{T}"/> does so automatically.
    /// </para>
    /// <para>
    /// <b>Composition.</b> A composite specification sets it when any operand sets it.
    /// </para>
    /// </remarks>
    bool AsNoTracking { get; }

    /// <summary>
    /// Gets a value indicating whether the evaluator must load <see cref="Includes"/> with one query per
    /// included collection instead of a single joined query.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Usage.</b> Set it when the specification includes two or more collections: a single joined query
    /// returns the Cartesian product of those collections, repeating rows. The default,
    /// <see langword="false"/>, is always correct and only potentially slower.
    /// </para>
    /// <para>
    /// <b>Composition.</b> A composite specification sets it when any operand sets it.
    /// </para>
    /// </remarks>
    bool AsSplitQuery { get; }

    /// <summary>
    /// Gets a value indicating whether the evaluator must bypass the global query filters so that soft-deleted
    /// entities are returned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Usage.</b> The default, <see langword="false"/>, hides soft-deleted entities. Set it only for admin,
    /// audit, export and recovery queries, never for user-facing queries.
    /// </para>
    /// <para>
    /// <b>Pitfall.</b> The bypass is not selective: it disables <em>every</em> global query filter on the
    /// entity, including tenant isolation. Always add the tenant condition back as criteria, for example
    /// <c>AddCriteria(o =&gt; o.TenantId == tenantId)</c>; criteria accumulate with AND, so this narrows the
    /// existing filter rather than replacing it.
    /// </para>
    /// <para>
    /// <b>Composition.</b> A composite specification sets it when any operand sets it, so combining with a
    /// single soft-delete-inclusive operand removes the tenant filter from the whole query.
    /// </para>
    /// </remarks>
    bool IncludeDeleted { get; }

    /// <summary>
    /// Gets the dot-separated navigation paths to eager-load, such as <c>"Lines.Product.Supplier"</c>; empty
    /// when none.
    /// </summary>
    /// <remarks>
    /// An evaluator loads these after <see cref="Includes"/> and before ordering. Use them for deep paths where
    /// expression includes become unwieldy. <see cref="Specification{T}"/> never stores a null or whitespace
    /// path.
    /// </remarks>
    IReadOnlyList<string> StringIncludes { get; }
}
