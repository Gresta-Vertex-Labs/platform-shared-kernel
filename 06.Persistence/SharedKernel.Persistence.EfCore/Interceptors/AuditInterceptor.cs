using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// EF Core save-changes interceptor that populates audit metadata before each commit.
/// </summary>
/// <remarks>
/// <para>
/// On <see cref="SavingChangesAsync"/> / <see cref="SavingChanges"/>:
/// <list type="bullet">
///   <item><description>
///     For entities in <see cref="EntityState.Added"/> state that implement
///     <see cref="IHasCreatedAudit"/>: sets <c>CreatedBy</c> and <c>CreatedOn</c>.
///   </description></item>
///   <item><description>
///     For entities in <see cref="EntityState.Modified"/> state that implement
///     <see cref="IHasAudit"/>: sets <c>ModifiedBy</c> and <c>ModifiedOn</c>.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Mutation rule:</strong> All field writes go exclusively through
/// <c>ChangeTracker.Entry(entity).CurrentValues[propertyName]</c>. Direct property setters on
/// entity instances are never called — doing so would bypass EF Core's change-tracking
/// and violate the aggregate root encapsulation contract.
/// </para>
/// <para>
/// <strong>Fallback strategy:</strong> When <see cref="IUserContext.UserId"/> is empty or
/// whitespace, the literal string <c>"system"</c> is used as the fallback identifier.
/// This covers background jobs and seeding operations where no HTTP context exists.
/// </para>
/// </remarks>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    /// <summary>
    /// Initialises a new <see cref="AuditInterceptor"/> with the required dependencies.
    /// </summary>
    /// <param name="userContext">
    /// Scoped DI dependency providing the current user's identifier. Never <see langword="null"/>.
    /// </param>
    /// <param name="clock">
    /// Abstracted system clock for deterministic timestamp production. Never <see langword="null"/>.
    /// </param>
    public AuditInterceptor(IUserContext userContext, IClock clock)
    {
        _userContext = userContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAudit(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAudit(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    // Applies audit fields to Added and Modified entries via EF ChangeTracker.
    private void ApplyAudit(DbContext? context)
    {
        if (context is null) return;

        var userId = string.IsNullOrWhiteSpace(_userContext.UserId) ? "system" : _userContext.UserId;
        var now = _clock.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added && entry.Entity is IHasCreatedAudit)
            {
                entry.CurrentValues[nameof(IHasCreatedAudit.CreatedBy)] = userId;
                entry.CurrentValues[nameof(IHasCreatedAudit.CreatedOn)] = now;
            }

            if (entry.State == EntityState.Modified && entry.Entity is IHasAudit)
            {
                entry.CurrentValues[nameof(IHasAudit.ModifiedBy)] = userId;
                entry.CurrentValues[nameof(IHasAudit.ModifiedOn)] = now;
            }
        }
    }
}
