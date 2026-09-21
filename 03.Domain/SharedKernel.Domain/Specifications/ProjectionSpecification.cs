using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A query specification that also projects each matching entity to <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="T">The entity type the query runs over.</typeparam>
/// <typeparam name="TResult">The projected type, typically a DTO.</typeparam>
/// <remarks>
/// The persistence layer applies <see cref="Selector"/> last, after filtering, ordering and paging, and
/// translates it to SQL so only the referenced columns are read. Keep the selector translatable: member
/// access, constructors and simple arithmetic, no method calls the provider cannot translate.
/// </remarks>
public interface IProjectionSpecification<T, TResult> : ISpecification<T>
{
    /// <summary>Gets the projection applied to every matching entity.</summary>
    Expression<Func<T, TResult>> Selector { get; }
}

/// <summary>
/// Base class for a named projection query: a subclass declares its filter and ordering like any
/// <see cref="Specification{T}"/> and its projection with <see cref="ApplySelector"/>.
/// </summary>
/// <typeparam name="T">The entity type the query runs over.</typeparam>
/// <typeparam name="TResult">The projected type, typically a DTO.</typeparam>
/// <example>
/// <code>
/// public sealed class OrderSummariesSpec : ProjectionSpecification&lt;Order, OrderSummary&gt;
/// {
///     public OrderSummariesSpec(Guid customerId)
///     {
///         AddCriteria(o =&gt; o.CustomerId == customerId);
///         ApplyOrderByDescending(o =&gt; o.CreatedOn);
///         ApplySelector(o =&gt; new OrderSummary(o.Id.Value, o.Total.Amount));
///     }
/// }
/// </code>
/// </example>
public abstract class ProjectionSpecification<T, TResult> : Specification<T>, IProjectionSpecification<T, TResult>
{
    private Expression<Func<T, TResult>>? _selector;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The subclass never called <see cref="ApplySelector"/>.</exception>
    public Expression<Func<T, TResult>> Selector =>
        _selector ?? throw new InvalidOperationException(
            $"{GetType().Name} has no selector. Call ApplySelector in its constructor.");

    /// <summary>Sets the projection. Call it once, from the subclass constructor.</summary>
    /// <param name="selector">The projection. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A selector is already set.</exception>
    protected void ApplySelector(Expression<Func<T, TResult>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (_selector is not null)
            throw new InvalidOperationException($"{GetType().Name} already has a selector.");

        _selector = selector;
    }
}

/// <summary>
/// The projection specification <see cref="SpecificationBuilder{T}.Select{TResult}"/> returns: the builder's
/// specification plus a selector.
/// </summary>
internal sealed class SelectedSpecification<T, TResult> : IProjectionSpecification<T, TResult>
{
    private readonly Specification<T> _inner;

    internal SelectedSpecification(Specification<T> inner, Expression<Func<T, TResult>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _inner = inner;
        Selector = selector;
    }

    public Expression<Func<T, TResult>> Selector { get; }

    public Expression<Func<T, bool>>? Criteria => _inner.Criteria;

    public IReadOnlyList<Expression<Func<T, object>>> Includes => _inner.Includes;

    public Expression<Func<T, object>>? OrderBy => _inner.OrderBy;

    public Expression<Func<T, object>>? OrderByDescending => _inner.OrderByDescending;

    public IReadOnlyList<(Expression<Func<T, object>> KeySelector, bool Descending)> ThenBys => _inner.ThenBys;

    public int? Skip => _inner.Skip;

    public int? Take => _inner.Take;

    public bool IsDistinct => _inner.IsDistinct;

    public bool AsSplitQuery => _inner.AsSplitQuery;

    public bool IncludeDeleted => _inner.IncludeDeleted;

    public IReadOnlyList<string> StringIncludes => _inner.StringIncludes;
}
