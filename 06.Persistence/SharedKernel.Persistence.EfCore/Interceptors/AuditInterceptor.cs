using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// EF Core save-changes interceptor that populates audit metadata before each commit.
/// </summary>
/// <remarks>
/// <para>
/// On <see cref="SavingChangesAsync"/> / <see cref="SavingChanges"/>:
/// <list type="bullet">
/// <item><description>
/// For entities in <see cref="EntityState.Added"/> state that implement
/// <see cref="IHasCreatedAudit"/>: sets <c>CreatedBy</c> and <c>CreatedOn</c>.
/// </description></item>
/// <item><description>
/// For entities in <see cref="EntityState.Modified"/> state that implement
/// <see cref="IHasAudit"/>: sets <c>ModifiedBy</c> and <c>ModifiedOn</c>.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Actor resolution:</strong> <c>CreatedBy</c>/<c>ModifiedBy</c> is written as
/// <see cref="ICurrentActorContext.ActorId"/> — the fallback-to-service-name rule that used to live
/// here (checking <c>IUserContext.IsAuthenticated</c>/<c>SubjectId</c>) now lives entirely inside
/// whichever <see cref="ICurrentActorContext"/> implementation is registered (the default
/// <see cref="AnonymousActorContext"/>, or a consuming service's real bridge over its identity
/// provider). This interceptor no longer references <c>SharedKernel.Security.Abstractions</c> at all.
/// </para>
/// <para>
/// <strong>Mutation rule:</strong> All field writes go exclusively through
/// <c>ChangeTracker.Entry(entity).CurrentValues[propertyName]</c>. Direct property setters on
/// entity instances are never called — doing so would bypass EF Core's change-tracking
/// and violate the aggregate root encapsulation contract.
/// </para>
/// </remarks>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentActorContext _actorContext;
    private readonly IClock _clock;

    /// <summary>
    /// Initialises a new <see cref="AuditInterceptor"/> with the required dependencies.
    /// </summary>
    /// <param name="actorContext">
    /// Scoped DI dependency providing the current actor's identity. Never <see langword="null"/>.
    /// </param>
    /// <param name="clock">
    /// Abstracted system clock for deterministic timestamp production. Never <see langword="null"/>.
    /// </param>
    public AuditInterceptor(ICurrentActorContext actorContext, IClock clock)
    {
        _actorContext = actorContext;
        _clock = clock;
    }

    /// <summary>
    /// Gets the <see cref="ICurrentActorContext"/> captured at construction time.
    /// </summary>
    /// <remarks>
    /// Exposed solely so <see cref="SharedKernelDbContext"/>'s constructor can
    /// initialise <see cref="SharedKernelDbContext.CurrentActor"/> from this instance without
    /// requiring a new, separately-injected constructor parameter on
    /// <see cref="SharedKernelDbContext"/> itself. This interceptor no longer reads this field
    /// directly inside <see cref="ApplyAudit"/> — see that method's remarks for why.
    /// </remarks>
    internal ICurrentActorContext ActorContext => _actorContext;

    /// <summary>Gets the clock captured at construction time.</summary>
    /// <remarks>
    /// Exposed so <see cref="SharedKernelDbContext"/> can give the same clock to
    /// <see cref="DomainClockMaterializationInterceptor"/> without a new constructor parameter, mirroring
    /// <see cref="ActorContext"/>.
    /// </remarks>
    internal IClock Clock => _clock;

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
    //
    // Resolves the current ICurrentActorContext LIVE off
    // ((SharedKernelDbContext)context).CurrentActor instead of this interceptor's own
    // constructor-captured _actorContext field. Under the default (non-pooled) registration this
    // produces an identical value to before, since SharedKernelDbContext.CurrentActor is
    // itself initialised from this same interceptor's captured ICurrentActorContext at construction
    // time. Under.WithDbContextPooling(), CurrentActor is refreshed per lease via
    // RefreshActor(...) — reading it here (rather than this interceptor's own frozen field,
    // which is never updated on lease) is what prevents a pooled instance from misattributing audit
    // fields to whichever request first constructed it.
    private void ApplyAudit(DbContext? context)
    {
        if (context is null) return;

        var actorContext = ((SharedKernelDbContext)context).CurrentActor;
        var userId = actorContext.ActorId;
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
