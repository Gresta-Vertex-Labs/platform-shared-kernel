using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Persistence.EfCore.Extensions;

/// <summary>
/// Extension methods on <see cref="IQueryable{T}"/> that expose EF Core query-filter bypass helpers.
/// </summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Bypasses the global soft-delete query filter (<c>e =&gt; !e.IsDeleted</c>) so that
    /// soft-deleted records are included in the results.
    /// </summary>
    /// <typeparam name="T">
    /// The entity type. Must implement <see cref="ISoftDeletable"/>.
    /// </typeparam>
    /// <param name="query">The source queryable to modify.</param>
    /// <returns>
    /// A new <see cref="IQueryable{T}"/> that includes soft-deleted records.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Admin-panel and audit use only.</strong> This method removes the soft-delete
    /// visibility barrier. Exposing deleted records to end-users or using this method in
    /// standard application queries is a data-integrity risk. Restrict callers to:
    /// <list type="bullet">
    ///   <item><description>Admin panels and back-office UIs.</description></item>
    ///   <item><description>Data-export and audit-trail reports.</description></item>
    ///   <item><description>Recovery and restore operations.</description></item>
    ///   <item><description>Data-migration scripts.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Under the hood this calls <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/>
    /// which removes <em>all</em> global query filters registered for the entity type,
    /// including any tenant isolation filter if the context is a <c>TenantedDbContext</c>.
    /// If you need to bypass only the soft-delete filter while preserving tenant isolation,
    /// filter on <c>e.IsDeleted == false</c> manually after calling this method.
    /// </para>
    /// </remarks>
    public static IQueryable<T> IgnoreSoftDeleteFilter<T>(this IQueryable<T> query)
        where T : class, ISoftDeletable
    {
        return query.IgnoreQueryFilters();
    }
}
