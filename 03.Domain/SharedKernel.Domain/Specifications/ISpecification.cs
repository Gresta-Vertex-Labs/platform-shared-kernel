using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Defines a query specification that encapsulates filtering, ordering, paging, and eager-loading
/// rules for a domain query against entities of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of domain entity this specification applies to.</typeparam>
/// <remarks>
/// <para>
/// Specifications are consumed by repository implementations in <c>06.Persistence</c> to build
/// LINQ or SQL queries. The domain layer defines the <em>what</em>; the persistence layer handles
/// the <em>how</em>.
/// </para>
/// <para>
/// <strong>Ordering precedence:</strong>
/// <list type="number">
///   <item><description>
///     <strong>Primary sort:</strong> either <see cref="OrderBy"/> or <see cref="OrderByDescending"/>
///     (mutually exclusive — calling both is allowed but the last call wins).
///   </description></item>
///   <item><description>
///     <strong>Secondary sorts:</strong> <see cref="ThenBys"/> entries, applied in the order
///     <c>ApplyThenBy</c> / <c>ApplyThenByDescending</c> were called.
///   </description></item>
///   <item><description>
///     If neither primary sort is set, <see cref="ThenBys"/> entries are ignored by
///     well-behaved repository implementations.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
public interface ISpecification<T>
{
    /// <summary>Gets the filter predicate applied to entities, or <see langword="null"/> when all entities match.</summary>
    Expression<Func<T, bool>>? Criteria { get; }

    /// <summary>Gets the list of navigation property include paths for eager loading.</summary>
    IReadOnlyList<Expression<Func<T, object>>> Includes { get; }

    /// <summary>Gets the primary ascending-order expression, or <see langword="null"/> when no ordering is applied.</summary>
    Expression<Func<T, object>>? OrderBy { get; }

    /// <summary>Gets the primary descending-order expression, or <see langword="null"/> when no ordering is applied.</summary>
    Expression<Func<T, object>>? OrderByDescending { get; }

    /// <summary>
    /// Gets the list of secondary sort expressions applied after the primary sort.
    /// Each entry carries the key selector and a flag indicating descending order.
    /// </summary>
    IReadOnlyList<(Expression<Func<T, object>> KeySelector, bool Descending)> ThenBys { get; }

    /// <summary>Gets the number of entities to skip (for paging), or <see langword="null"/> when paging is not applied.</summary>
    int? Skip { get; }

    /// <summary>Gets the maximum number of entities to return (page size), or <see langword="null"/> when paging is not applied.</summary>
    int? Take { get; }

    /// <summary>Gets a value indicating whether duplicate results should be eliminated.</summary>
    bool IsDistinct { get; }

    /// <summary>
    /// Gets a value indicating whether the consuming repository should apply change-tracking suppression
    /// (e.g., <c>AsNoTracking()</c>) to the underlying query.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="false"/> — safe for specifications used before write operations.
    /// Set to <see langword="true"/> for read-only query specifications to avoid unnecessary
    /// change-tracking overhead. Composite specifications propagate <see langword="true"/> if either
    /// operand carries <see langword="true"/> (more restrictive wins).
    /// </remarks>
    bool AsNoTracking { get; }
}
