using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// EF Core save-changes interceptor that converts physical deletes into soft deletes for
/// entities implementing <see cref="ISoftDeletable"/>.
/// </summary>
/// <remarks>
/// <para>
/// On <see cref="SavingChangesAsync"/> / <see cref="SavingChanges"/>:
/// for every entity entry in <see cref="EntityState.Deleted"/> state whose entity implements
/// <see cref="ISoftDeletable"/>, this interceptor:
/// <list type="number">
///   <item><description>Changes the entry state from <c>Deleted</c> to <c>Modified</c>.</description></item>
///   <item><description>Sets <c>IsDeleted = true</c>.</description></item>
///   <item><description>Sets <c>DeletedOn</c> to the current UTC timestamp.</description></item>
///   <item><description>Sets <c>DeletedBy</c> to the current user identifier.</description></item>
/// </list>
/// </para>
/// <para>
/// Non-<see cref="ISoftDeletable"/> entities in <c>Deleted</c> state pass through without
/// modification — their rows are physically removed.
/// </para>
/// <para>
/// <strong>Mutation rule:</strong> All field writes go exclusively through
/// <c>ChangeTracker.Entry(entity).CurrentValues[propertyName]</c>. Direct property setters on
/// entity instances are never called.
/// </para>
/// <para>
/// Soft-deleted records are hidden from normal queries by the global query filter
/// <c>e =&gt; !e.IsDeleted</c> configured by <c>EntityTypeConfigurationBase</c>. To query
/// soft-deleted records, use <c>.IgnoreQueryFilters()</c> on the queryable.
/// </para>
/// </remarks>
public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
{
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    /// <summary>
    /// Initialises a new <see cref="SoftDeleteInterceptor"/> with the required dependencies.
    /// </summary>
    /// <param name="userContext">
    /// Scoped DI dependency providing the current user's identifier.
    /// </param>
    /// <param name="clock">
    /// Abstracted system clock for deterministic timestamp production.
    /// </param>
    public SoftDeleteInterceptor(IUserContext userContext, IClock clock)
    {
        _userContext = userContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplySoftDelete(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplySoftDelete(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    // Converts Deleted state to Modified for ISoftDeletable entities.
    private void ApplySoftDelete(DbContext? context)
    {
        if (context is null) return;

        var userId = string.IsNullOrWhiteSpace(_userContext.UserId) ? "system" : _userContext.UserId;
        var now = _clock.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>()
            .Where(e => e.State == EntityState.Deleted))
        {
            entry.State = EntityState.Modified;
            entry.CurrentValues[nameof(ISoftDeletable.IsDeleted)] = true;
            entry.CurrentValues[nameof(ISoftDeletable.DeletedOn)] = now;
            entry.CurrentValues[nameof(ISoftDeletable.DeletedBy)] = userId;
        }
    }
}
