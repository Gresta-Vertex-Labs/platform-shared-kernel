using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions.Abstractions;

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
/// <strong>Audit string format (P-091, updated WO-019):</strong> <c>DeletedBy</c> is populated
/// using the same adapter as <c>AuditInterceptor</c>: <c>userId.ToString("D")</c> (lowercase
/// hyphenated GUID) when <c>IsAuthenticated == true</c> and <c>UserId != Guid.Empty</c>;
/// <c>PersistenceServiceOptions.ServiceName</c> (default <c>"system"</c>) otherwise.
/// Audit columns carry <c>HasMaxLength(256)</c>, which accommodates both formats.
/// </para>
/// <para>
/// <strong>Mutation rule:</strong> All field writes go exclusively through
/// <c>ChangeTracker.Entry(entity).CurrentValues[propertyName]</c>. Direct property setters on
/// entity instances are never called.
/// </para>
/// <para>
/// Soft-deleted records are hidden from normal queries by the global query filter
/// <c>e =&gt; !e.IsDeleted</c> configured by <c>EntityTypeConfigurationBase</c>.
/// Use <c>spec.IncludeDeleted = true</c> on a specification to bypass this filter via the
/// <c>SpecificationEvaluator</c>; do not call <c>.IgnoreQueryFilters()</c> directly.
/// </para>
/// </remarks>
public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
{
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IOptions<PersistenceServiceOptions> _serviceOptions;

    /// <summary>
    /// Initialises a new <see cref="SoftDeleteInterceptor"/> with the required dependencies.
    /// </summary>
    /// <param name="userContext">
    /// Scoped DI dependency providing the current user's identity.
    /// </param>
    /// <param name="clock">
    /// Abstracted system clock for deterministic timestamp production.
    /// </param>
    /// <param name="serviceOptions">
    /// Options providing the unauthenticated audit fallback string (defaults to <c>"system"</c>).
    /// </param>
    public SoftDeleteInterceptor(
        IUserContext userContext,
        IClock clock,
        IOptions<PersistenceServiceOptions> serviceOptions)
    {
        _userContext = userContext;
        _clock = clock;
        _serviceOptions = serviceOptions;
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

        var userId = ResolveUserId();
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

    // Resolves the audit string from the current IUserContext per P-091/WO-019 rules.
    private string ResolveUserId()
        => _userContext.IsAuthenticated && _userContext.UserId != Guid.Empty
            ? _userContext.UserId.ToString("D")
            : _serviceOptions.Value.ServiceName;
}
