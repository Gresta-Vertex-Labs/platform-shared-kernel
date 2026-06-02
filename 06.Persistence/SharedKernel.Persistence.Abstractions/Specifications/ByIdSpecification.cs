using System.Linq.Expressions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.Abstractions.Specifications;

/// <summary>
/// Canonical specification that filters aggregates by their unique identity.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must implement <see cref="IAggregateRoot{TId}"/>.
/// </typeparam>
/// <typeparam name="TId">The aggregate's identity type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// This is the canonical replacement for the removed <c>IReadRepository.GetByIdAsync</c>
/// (P-080 breaking change). Migration:
/// <code>
/// // Before (no longer compiles):
/// await readRepo.GetByIdAsync(id, ct);
///
/// // After:
/// await readRepo.GetBySpecAsync(new ByIdSpecification&lt;TAggregate, TId&gt;(id), ct);
/// </code>
/// </para>
/// <para>
/// Uses <c>e.Id.Equals(id)</c> as an expression tree — AOT-safe on <see cref="System.Linq.IQueryable{T}"/>.
/// </para>
/// </remarks>
public sealed class ByIdSpecification<TAggregate, TId> : Specification<TAggregate>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>
    /// Initialises a new <see cref="ByIdSpecification{TAggregate, TId}"/> that matches the
    /// aggregate with the given <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The identity value to match.</param>
    public ByIdSpecification(TId id)
    {
        AddCriteria(BuildCriteria(id));
    }

    // Builds the expression tree e => e.Id.Equals(id) without reflection.
    private static Expression<Func<TAggregate, bool>> BuildCriteria(TId id)
    {
        var param = Expression.Parameter(typeof(TAggregate), "e");
        var idProperty = Expression.Property(param, "Id");
        var idValue = Expression.Constant(id, typeof(TId));
        var equalsCall = Expression.Call(idProperty, "Equals", null, idValue);
        return Expression.Lambda<Func<TAggregate, bool>>(equalsCall, param);
    }
}
