using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Domain.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Options;
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
/// <strong>Actor resolution:</strong> <c>CreatedBy</c>/<c>ModifiedBy</c> is written as the executing
/// context's <see cref="SharedKernelDbContext.RequestContext"/> <see cref="IRequestContext.UserId"/>,
/// falling back to <see cref="PersistenceServiceOptions.ServiceName"/> (default <c>"system"</c>) when
/// the caller has no user id — an anonymous request or a background job.
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
    private readonly IClock _clock;
    private readonly IOptions<PersistenceServiceOptions>? _serviceOptions;

    /// <summary>
    /// Initialises a new <see cref="AuditInterceptor"/> with the required dependencies.
    /// </summary>
    /// <param name="requestContext">
    /// The caller in the scope that constructed this interceptor — the initial
    /// <see cref="SharedKernelDbContext.RequestContext"/> of a context built with it.
    /// </param>
    /// <param name="clock">Abstracted system clock for deterministic timestamp production.</param>
    /// <param name="serviceOptions">
    /// Supplies the service-name fallback written when the caller has no user id. When
    /// <see langword="null"/>, <c>"system"</c> is written.
    /// </param>
    public AuditInterceptor(
        IRequestContext requestContext,
        IClock clock,
        IOptions<PersistenceServiceOptions>? serviceOptions = null)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(clock);

        RequestContext = requestContext;
        _clock = clock;
        _serviceOptions = serviceOptions;
    }

    /// <summary>Gets the request context captured at construction time.</summary>
    /// <remarks>
    /// Exposed solely so <see cref="SharedKernelDbContext"/>'s constructor can initialise
    /// <see cref="SharedKernelDbContext.RequestContext"/> without a separate constructor parameter.
    /// Never read inside <see cref="ApplyAudit"/>, which uses the executing context's live value.
    /// </remarks>
    internal IRequestContext RequestContext { get; }

    /// <summary>Gets the service-name fallback written when the caller has no user id.</summary>
    internal string ServiceName => _serviceOptions?.Value.ServiceName ?? new PersistenceServiceOptions().ServiceName;

    /// <summary>Gets the clock captured at construction time.</summary>
    /// <remarks>
    /// Exposed so <see cref="SharedKernelDbContext"/> can give the same clock to
    /// <see cref="DomainClockMaterializationInterceptor"/> without a new constructor parameter.
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

    // Applies audit fields to Added and Modified entries via EF ChangeTracker. The actor is read LIVE
    // off the executing context (never a constructor-captured value), so a pooled instance attributes
    // each save to the lease actually performing it.
    private void ApplyAudit(DbContext? context)
    {
        if (context is null) return;

        var userId = ((SharedKernelDbContext)context).CurrentActorId;
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
