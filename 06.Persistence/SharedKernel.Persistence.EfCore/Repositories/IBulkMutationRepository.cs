using Microsoft.EntityFrameworkCore.Query;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// Set-based bulk mutation operations over rows matching an <see cref="ISpecification{T}"/>,
/// translated to a single server-side <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> SQL statement.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must implement <see cref="IAggregateRoot{TId}"/>.
/// </typeparam>
/// <typeparam name="TId">The aggregate's identity type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// Lives in <c>SharedKernel.Persistence.EfCore</c> (not <c>.Abstractions</c>) because
/// <see cref="UpdateSettersBuilder{TSource}"/> is an EF Core type
/// (<c>Microsoft.EntityFrameworkCore.Query</c>) — placing this interface in
/// <c>SharedKernel.Persistence.Abstractions</c> would introduce an ORM dependency there.
/// </para>
/// <para>
/// <strong>Bypass warning:</strong> both methods bypass the EF Core change tracker entirely. As a
/// direct consequence:
/// <list type="bullet">
/// <item><description><see cref="SharedKernel.Persistence.Abstractions.UnitOfWork.IUnitOfWork.SaveChangesAsync"/> is NOT invoked and has no effect on these rows.</description></item>
/// <item><description>The three platform interceptors (Audit, SoftDelete, Concurrency) do NOT run.</description></item>
/// <item><description>Domain events are NOT collected or dispatched for affected aggregates.</description></item>
/// </list>
/// <see cref="ExecuteDeleteAsync"/> always issues a hard physical <c>DELETE</c>, even when
/// <typeparamref name="TAggregate"/> implements
/// <see cref="SharedKernel.Domain.Abstractions.ISoftDeletable"/> — there is no server-side
/// translation for "set <c>IsDeleted = true</c>" semantics via <c>ExecuteDelete</c>. Callers needing
/// soft-delete semantics in bulk must use <see cref="ExecuteUpdateAsync"/> with an explicit
/// <c>setPropertyCalls</c> delegate that sets the <c>IsDeleted</c>/<c>DeletedOn</c> columns.
/// </para>
/// </remarks>
public interface IBulkMutationRepository<TAggregate, TId>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>
    /// Executes a single server-side <c>UPDATE</c> statement over all rows matching
    /// <paramref name="spec"/>.
    /// </summary>
    /// <param name="spec">
    /// The specification describing which rows to update. Only
    /// <see cref="ISpecification{T}.Criteria"/> and <see cref="ISpecification{T}.IncludeDeleted"/>
    /// are honored — <see cref="ISpecification{T}.Includes"/>, <see cref="ISpecification{T}.StringIncludes"/>,
    /// ordering, and paging are rejected with <see cref="UnsupportedSpecificationException"/>.
    /// </param>
    /// <param name="setPropertyCalls">
    /// A delegate describing which columns to set and to what values, via
    /// <see cref="UpdateSettersBuilder{TSource}.SetProperty{TProperty}(System.Linq.Expressions.Expression{Func{TSource,TProperty}}, TProperty)"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows updated.</returns>
    Task<int> ExecuteUpdateAsync(
        ISpecification<TAggregate> spec,
        Action<UpdateSettersBuilder<TAggregate>> setPropertyCalls,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a single server-side hard physical <c>DELETE</c> statement over all rows matching
    /// <paramref name="spec"/>.
    /// </summary>
    /// <param name="spec">
    /// The specification describing which rows to delete. Only
    /// <see cref="ISpecification{T}.Criteria"/> and <see cref="ISpecification{T}.IncludeDeleted"/>
    /// are honored — <see cref="ISpecification{T}.Includes"/>, <see cref="ISpecification{T}.StringIncludes"/>,
    /// ordering, and paging are rejected with <see cref="UnsupportedSpecificationException"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows deleted.</returns>
    Task<int> ExecuteDeleteAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);
}
