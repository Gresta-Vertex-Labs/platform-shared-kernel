using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A query over entities of type <typeparamref name="T"/>: the filter, eager loading, ordering and
/// query-shape flags a persistence-layer query evaluator translates into one database query.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <remarks>
/// <para>
/// <b>Usage.</b> Build one inline with <see cref="Spec.For{T}"/>, or derive a named, reusable query from
/// <see cref="Specification{T}"/> (or <see cref="ProjectionSpecification{T, TResult}"/> for a DTO query). Both
/// enforce the invariants below. The domain declares what to fetch; the evaluator decides how.
/// </para>
/// <para>
/// <b>Paging belongs to the call site.</b> Page a query by passing a page request to the repository
/// (offset: <c>ListPagedAsync(spec, pageRequest)</c>; keyset: <c>ListKeysetAsync(spec, cursorRequest,
/// keySelector)</c>), not by building paging into the specification. <see cref="Skip"/> and
/// <see cref="Take"/> remain for fixed windows such as "the ten most recent".
/// </para>
/// <para>
/// <b>Tracking belongs to the repository.</b> A read repository never tracks; a write repository always
/// does. A specification carries no tracking flag.
/// </para>
/// <para>
/// <b>Evaluation contract.</b> An evaluator must, in this order:
/// <list type="number">
///   <item><description>Bypass the soft-delete filter (and only that filter) when <see cref="IncludeDeleted"/> is
///   <see langword="true"/>.</description></item>
///   <item><description>Filter by <see cref="Criteria"/>, when not <see langword="null"/>.</description></item>
///   <item><description>Eager-load <see cref="Includes"/>, then <see cref="StringIncludes"/>, and split the
///   query when <see cref="AsSplitQuery"/> is <see langword="true"/>.</description></item>
///   <item><description>Apply <see cref="IsDistinct"/>, before ordering.</description></item>
///   <item><description>Sort by the one primary sort (<see cref="OrderBy"/> or <see cref="OrderByDescending"/>),
///   then by each <see cref="ThenBys"/> key in list order.</description></item>
///   <item><description>Apply <see cref="Skip"/> and then <see cref="Take"/> last; both require a primary
///   sort.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Composition.</b> <see cref="AndSpecification{T}"/>, <see cref="OrSpecification{T}"/> and
/// <see cref="NotSpecification{T}"/> combine criteria and merge the query shape; they never drop ordering or
/// paging silently — see <see cref="SpecificationExtensions"/>.
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
    /// <remarks>
    /// A selector may be a filtered include such as <c>o =&gt; o.Lines.Where(l =&gt; l.IsActive)</c>. Paths
    /// continued with <c>ThenInclude</c> are recorded in <see cref="StringIncludes"/>.
    /// </remarks>
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
    /// <see cref="Specification{T}"/> only accepts a secondary key once a primary sort is set, so this list is
    /// empty whenever <see cref="OrderBy"/> and <see cref="OrderByDescending"/> are both <see langword="null"/>.
    /// </remarks>
    IReadOnlyList<(Expression<Func<T, object>> KeySelector, bool Descending)> ThenBys { get; }

    /// <summary>
    /// Gets the number of ordered rows to skip, or <see langword="null"/> when none are skipped.
    /// </summary>
    /// <remarks>
    /// For page-by-page access pass a page request to the repository instead; a repository rejects a
    /// specification that pages itself when it is also given a page request.
    /// </remarks>
    int? Skip { get; }

    /// <summary>
    /// Gets the maximum number of rows to return, or <see langword="null"/> when unlimited.
    /// </summary>
    /// <remarks>When set through <see cref="Specification{T}"/>, the value is at least 1.</remarks>
    int? Take { get; }

    /// <summary>Gets a value indicating whether duplicate rows are removed from the result.</summary>
    bool IsDistinct { get; }

    /// <summary>
    /// Gets a value indicating whether the evaluator must load <see cref="Includes"/> with one query per
    /// included collection instead of a single joined query.
    /// </summary>
    /// <remarks>
    /// Set it when the specification includes two or more collections: a single joined query returns the
    /// Cartesian product of those collections, repeating rows. The default, <see langword="false"/>, is always
    /// correct and only potentially slower.
    /// </remarks>
    bool AsSplitQuery { get; }

    /// <summary>
    /// Gets a value indicating whether soft-deleted entities are returned as well.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Usage.</b> The default, <see langword="false"/>, hides soft-deleted entities. Set it for admin,
    /// audit, export and recovery queries, including loading a deleted aggregate to restore it.
    /// </para>
    /// <para>
    /// <b>Tenant isolation.</b> The persistence layer bypasses only the soft-delete filter; the tenant filter
    /// stays in force. Crossing tenants needs the explicit cross-tenant scope of the persistence layer.
    /// </para>
    /// </remarks>
    bool IncludeDeleted { get; }

    /// <summary>
    /// Gets the dot-separated navigation paths to eager-load, such as <c>"Lines.Product.Supplier"</c>; empty
    /// when none.
    /// </summary>
    /// <remarks>
    /// Holds both paths added as strings and the paths recorded by a typed <c>ThenInclude</c> chain. An
    /// evaluator loads these after <see cref="Includes"/> and before ordering. <see cref="Specification{T}"/>
    /// never stores a null or whitespace path.
    /// </remarks>
    IReadOnlyList<string> StringIncludes { get; }
}
