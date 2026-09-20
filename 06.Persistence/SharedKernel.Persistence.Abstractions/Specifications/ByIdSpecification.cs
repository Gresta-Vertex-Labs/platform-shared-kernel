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
/// (breaking change). Migration:
/// <code>
/// // Before (no longer compiles):
/// await readRepo.GetByIdAsync(id, cancellationToken);
///
/// // After:
/// await readRepo.GetBySpecAsync(new ByIdSpecification&lt;TAggregate, TId&gt;(id), cancellationToken);
/// </code>
/// </para>
/// <para>
/// Uses <c>e.Id.Equals(id)</c> as an expression tree — AOT-safe on <see cref="System.Linq.IQueryable{T}"/>.
/// </para>
/// <para>
/// <strong>Parameterized:</strong> <paramref name="id"/> is captured through a small
/// closure-holder field access (<c>Expression.Field(Expression.Constant(holder),...)</c>) rather
/// than a bare <c>Expression.Constant(id, typeof(TId))</c>. EF Core's query-parameter extraction
/// recognizes the former shape — the same one the C# compiler emits for an ordinary captured lambda
/// variable — and lifts it into a SQL parameter; a bare <c>ConstantExpression</c> is inlined as a
/// SQL literal instead, defeating query-plan caching (a distinct plan per distinct id value).
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

    // Builds the expression tree e => e.Id.Equals(holder.Id) without reflection, where "holder.Id"
    // is a closure-shaped field access so EF Core parameterizes it instead of inlining a literal —
    // see the class remarks above.
    private static Expression<Func<TAggregate, bool>> BuildCriteria(TId id)
    {
        var holder = new IdHolder(id);
        var param = Expression.Parameter(typeof(TAggregate), "e");
        var idProperty = Expression.Property(param, "Id");
        var idValue = Expression.Field(Expression.Constant(holder), nameof(IdHolder.Id));
        // Expression.Equal translates to SQL "=" which is correct and unambiguous.
        var equal = Expression.Equal(idProperty, idValue);
        return Expression.Lambda<Func<TAggregate, bool>>(equal, param);
    }

    // Mimics the shape of a compiler-generated closure display class so the captured value is
    // recognized and parameterized by EF Core's ParameterExtractingExpressionVisitor.
    private sealed class IdHolder(TId id)
    {
        public readonly TId Id = id;
    }
}
