using System.Linq.Expressions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Specifications;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// Fluent builder producing <see cref="IProjectionSpecification{TAggregate, TResult}"/> instances
/// without a full concrete specification class per test.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type the specification queries.</typeparam>
/// <typeparam name="TResult">The projection output type.</typeparam>
public sealed class ProjectionSpecificationBuilder<TAggregate, TResult>
{
    private Expression<Func<TAggregate, bool>>? _criteria;
    private Expression<Func<TAggregate, TResult>>? _selector;

    /// <summary>Sets the filter predicate.</summary>
    /// <param name="criteria">The filter predicate.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public ProjectionSpecificationBuilder<TAggregate, TResult> WithCriteria(Expression<Func<TAggregate, bool>> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        _criteria = criteria;
        return this;
    }

    /// <summary>Sets the projection selector.</summary>
    /// <param name="selector">The projection selector.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public ProjectionSpecificationBuilder<TAggregate, TResult> WithSelector(Expression<Func<TAggregate, TResult>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = selector;
        return this;
    }

    /// <summary>Builds the configured <see cref="IProjectionSpecification{TAggregate, TResult}"/>.</summary>
    /// <returns>A new specification instance.</returns>
    /// <exception cref="InvalidOperationException"><see cref="WithSelector"/> was never called.</exception>
    public IProjectionSpecification<TAggregate, TResult> Build()
    {
        if (_selector is null)
            throw new InvalidOperationException("WithSelector(...) must be called before Build().");

        return new BuiltProjectionSpecification(_criteria, _selector);
    }

    private sealed class BuiltProjectionSpecification : Specification<TAggregate>, IProjectionSpecification<TAggregate, TResult>
    {
        public BuiltProjectionSpecification(
            Expression<Func<TAggregate, bool>>? criteria,
            Expression<Func<TAggregate, TResult>> selector)
        {
            if (criteria is not null)
                AddCriteria(criteria);

            Selector = selector;
        }

        public Expression<Func<TAggregate, TResult>> Selector { get; }
    }
}
