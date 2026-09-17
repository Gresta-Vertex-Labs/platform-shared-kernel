using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;

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
/// <strong>Audit string format (P-091, updated WO-019):</strong> The audit column value is:
/// <list type="bullet">
///   <item><description>
///     <c>IUserContext.SubjectId</c> (the identity provider's subject, an API key's or certificate's client id;
///     at most 255 characters for OIDC) when the caller is authenticated and has a subject.
///   </description></item>
///   <item><description>
///     <c>PersistenceServiceOptions.ServiceName</c> (default <c>"system"</c>) otherwise —
///     unauthenticated, background jobs, seeding operations.
///   </description></item>
/// </list>
/// Audit columns are configured with <c>HasMaxLength(256)</c> which accommodates both formats.
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
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IOptions<PersistenceServiceOptions> _serviceOptions;

    /// <summary>
    /// Initialises a new <see cref="AuditInterceptor"/> with the required dependencies.
    /// </summary>
    /// <param name="userContext">
    /// Scoped DI dependency providing the current user's identity. Never <see langword="null"/>.
    /// </param>
    /// <param name="clock">
    /// Abstracted system clock for deterministic timestamp production. Never <see langword="null"/>.
    /// </param>
    /// <param name="serviceOptions">
    /// Options providing the unauthenticated audit fallback string (defaults to <c>"system"</c>).
    /// </param>
    public AuditInterceptor(
        IUserContext userContext,
        IClock clock,
        IOptions<PersistenceServiceOptions> serviceOptions)
    {
        _userContext = userContext;
        _clock = clock;
        _serviceOptions = serviceOptions;
    }

    /// <summary>
    /// Gets the <see cref="IUserContext"/> captured at construction time.
    /// </summary>
    /// <remarks>
    /// WO-051/P-322 — exposed solely so <see cref="SharedKernelDbContext"/>'s constructor can
    /// initialise <see cref="SharedKernelDbContext.CurrentUserContext"/> from this instance without
    /// requiring a new, separately-injected <c>IUserContext</c> constructor parameter on
    /// <see cref="SharedKernelDbContext"/> itself. This interceptor no longer reads this field
    /// directly inside <see cref="ApplyAudit"/> — see that method's remarks for why.
    /// </remarks>
    internal IUserContext UserContext => _userContext;

    /// <summary>Gets the clock captured at construction time.</summary>
    /// <remarks>
    /// Exposed so <see cref="SharedKernelDbContext"/> can give the same clock to
    /// <see cref="DomainClockMaterializationInterceptor"/> without a new constructor parameter, mirroring
    /// <see cref="UserContext"/>.
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
    // WO-051/P-322: resolves the current IUserContext LIVE off
    // ((SharedKernelDbContext)context).CurrentUserContext instead of this interceptor's own
    // constructor-captured _userContext field. Under the default (non-pooled) registration this
    // produces an identical value to before, since SharedKernelDbContext.CurrentUserContext is
    // itself initialised from this same interceptor's captured IUserContext at construction time.
    // Under .WithDbContextPooling(), CurrentUserContext is refreshed per lease via
    // RefreshUserContext(...) — reading it here (rather than this interceptor's own frozen field,
    // which is never updated on lease) is what prevents a pooled instance from misattributing audit
    // fields to whichever request first constructed it.
    private void ApplyAudit(DbContext? context)
    {
        if (context is null) return;

        var userContext = ((SharedKernelDbContext)context).CurrentUserContext;
        var userId = ResolveUserId(userContext);
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

    // Resolves the audit string from the given IUserContext per P-091/WO-019 rules.
    private string ResolveUserId(IUserContext userContext)
        => userContext.IsAuthenticated && userContext.SubjectId is { } subjectId
            ? subjectId
            : _serviceOptions.Value.ServiceName;
}
