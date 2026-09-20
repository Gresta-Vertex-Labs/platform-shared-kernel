using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// EF Core save-changes interceptor holding the rule that translates
/// <see cref="DbUpdateConcurrencyException"/> into a typed <see cref="ConflictException"/> for
/// entities implementing <see cref="IHasConcurrency"/>, or a typed <see cref="ForbiddenException"/>
/// for entities implementing <see cref="IHasTenant"/> — see <see cref="TryTranslate"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why translation happens here, not in <c>SaveChangesFailed</c>:</strong> this
/// interceptor originally performed the translation directly inside its
/// <see cref="SaveChangesFailed"/>/<see cref="SaveChangesFailedAsync"/> overrides, on the
/// assumption that an exception thrown from those hooks would replace the exception propagating
/// out of <c>DbContext.SaveChanges</c>/<c>SaveChangesAsync</c>. A real <see cref="IHasConcurrency"/>
/// SQLite test fixture (see <c>ConcurrencyInterceptorTests</c>) proved that assumption FALSE against
/// EF Core 10.0.5, using the platform's real interceptor registration shape:
/// <strong>any exception thrown from <c>SaveChangesFailed</c>/<c>SaveChangesFailedAsync</c> is
/// swallowed by EF Core's own interceptor dispatcher</strong> — confirmed by throwing an
/// unconditional, unrelated exception from these hooks and observing the ORIGINAL
/// <see cref="DbUpdateConcurrencyException"/> still propagate to the caller unchanged. These two
/// hooks are diagnostic/notification-only in EF Core 10, unlike <c>SavingChanges</c>/
/// <c>SavingChangesAsync</c>, which DO support suppression via <c>InterceptionResult&lt;int&gt;</c>.
/// </para>
/// <para>
/// <strong>The fix:</strong> the translation RULE stays here as <see cref="TryTranslate"/> (single
/// source of truth for "which exception, which entries, which <see cref="Error"/>"), but the
/// actual ENFORCEMENT point is
/// <see cref="SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext.SaveChanges(bool)"/> and
/// <see cref="SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext.SaveChangesAsync(bool, CancellationToken)"/>,
/// which wrap the base call in a try/catch and call <see cref="TryTranslate"/> directly — the only
/// mechanism EF Core 10 actually honors for replacing a propagating <c>SaveChanges</c> exception.
/// <see cref="SaveChangesFailed"/>/<see cref="SaveChangesFailedAsync"/> are retained as harmless
/// base-delegating overrides for interceptor-pipeline symmetry (this class remains one of the
/// platform's always-on registered interceptors) — they perform no translation of their own.
/// </para>
/// <para>
/// <strong>No retry logic.</strong> Conflict resolution is the application layer's
/// responsibility — <see cref="TryTranslate"/> surfaces the conflict and stops. Callers that
/// require retry behaviour must implement it in a MediatR pipeline behavior or equivalent.
/// </para>
/// <para>
/// Non-concurrency exceptions and <see cref="DbUpdateConcurrencyException"/> instances that involve
/// neither an <see cref="IHasConcurrency"/> nor an <see cref="IHasTenant"/> entry are left
/// untranslated by <see cref="TryTranslate"/> (returns <see langword="null"/>) and propagate
/// unchanged.
/// </para>
/// <para>
/// <strong>Structured logging:</strong> <see cref="TryTranslate"/> is a
/// non-static instance method specifically so it can log via this interceptor's own injected
/// <see cref="ILogger{TCategoryName}"/> — a <c>ConcurrencyConflictDetected</c> Warning
/// (EventId <c>6000</c>) is emitted immediately before the translated exception is returned, naming
/// only the conflicting entry's CLR type — never the row payload.
/// <see cref="Context.SharedKernelDbContext"/>'s catch-filter calls this method via the
/// <c>ConcurrencyInterceptor</c> held on its own <c>PersistenceContextDependencies</c>, so both
/// <c>SaveChanges</c> and <c>SaveChangesAsync</c> pick up the logging automatically.
/// </para>
/// </remarks>
public sealed class ConcurrencyInterceptor : SaveChangesInterceptor
{
    private readonly ILogger<ConcurrencyInterceptor> _logger;

    /// <summary>
    /// Initialises a new <see cref="ConcurrencyInterceptor"/>.
    /// </summary>
    /// <param name="logger">
    /// Optional logger for the <c>ConcurrencyConflictDetected</c> Warning (EventId <c>6000</c>).
    /// Resolved by DI when registered; falls back to <see cref="NullLogger{T}"/> otherwise so every
    /// existing <c>new ConcurrencyInterceptor()</c> call site remains source-compatible.
    /// </param>
    public ConcurrencyInterceptor(ILogger<ConcurrencyInterceptor>? logger = null)
    {
        _logger = logger ?? NullLogger<ConcurrencyInterceptor>.Instance;
    }

    /// <inheritdoc />
    /// <remarks>
    /// No-op beyond the base implementation — see the class remarks for why this hook cannot
    /// replace the propagating exception in EF Core 10.
    /// </remarks>
    public override void SaveChangesFailed(DbContextErrorEventData eventData) =>
        base.SaveChangesFailed(eventData);

    /// <inheritdoc />
    /// <remarks>
    /// No-op beyond the base implementation — see the class remarks for why this hook cannot
    /// replace the propagating exception in EF Core 10.
    /// </remarks>
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default) =>
        base.SaveChangesFailedAsync(eventData, cancellationToken);

    /// <summary>
    /// Translates <paramref name="exception"/> into a <see cref="ConflictException"/> when it is a
    /// <see cref="DbUpdateConcurrencyException"/> affecting at least one <see cref="IHasConcurrency"/>
    /// entry, or into a <see cref="ForbiddenException"/> when it affects at least one
    /// <see cref="IHasTenant"/> entry but no <see cref="IHasConcurrency"/> one.
    /// </summary>
    /// <param name="exception">The exception observed at the <c>SaveChanges</c> call boundary.</param>
    /// <returns>
    /// A <see cref="ConflictException"/> or <see cref="ForbiddenException"/> wrapping
    /// <paramref name="exception"/> as its inner exception when translation applies; otherwise
    /// <see langword="null"/> — the caller must let <paramref name="exception"/> propagate unchanged
    /// in that case.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Tenant-isolation translation:</strong> <c>MultiTenancy.TenantedDbContext.ApplyTenantFilters</c>
    /// marks every tenanted root entity's <c>TenantId</c> as an EF Core concurrency token, so an
    /// UPDATE/DELETE whose <c>TenantId</c> no longer matches the row it targets (a detached entity
    /// carrying the caller's own tenant id but another tenant's primary key —
    /// <see cref="TenantWriteGuardInterceptor"/>'s in-memory check cannot see this shape, since the
    /// stub's claimed <c>TenantId</c> does equal the caller's tenant) surfaces as a
    /// <see cref="DbUpdateConcurrencyException"/> here rather than silently affecting the wrong row.
    /// Checked only when no entry implements <see cref="IHasConcurrency"/>, so a genuine, unrelated
    /// optimistic-concurrency conflict on a tenanted aggregate is never misreported as a tenant
    /// violation.
    /// </para>
    /// </remarks>
    internal SharedKernelException? TryTranslate(Exception exception)
    {
        if (exception is not DbUpdateConcurrencyException concurrencyEx)
            return null;

        var concurrencyEntry = concurrencyEx.Entries.FirstOrDefault(e => e.Entity is IHasConcurrency);

        if (concurrencyEntry is not null)
        {
            PersistenceLog.ConcurrencyConflictDetected(_logger, concurrencyEntry.Entity.GetType().Name);
            Diagnostics.PersistenceMeter.ConcurrencyConflicts.Add(1,
                new KeyValuePair<string, object?>(Diagnostics.PersistenceTagKeys.AggregateType, concurrencyEntry.Entity.GetType().Name));

            return new ConflictException(
                Error.Conflict(
                    "persistence.concurrency_conflict",
                    "A concurrency conflict occurred. The entity was modified by another process. Reload and retry."),
                concurrencyEx);
        }

        var tenantEntry = concurrencyEx.Entries.FirstOrDefault(e => e.Entity is IHasTenant);

        if (tenantEntry is not null)
        {
            var entityTypeName = tenantEntry.Entity.GetType().Name;
            PersistenceLog.ConcurrencyConflictDetected(_logger, entityTypeName);
            Diagnostics.PersistenceMeter.TenantIsolationViolations.Add(1,
                new KeyValuePair<string, object?>(Diagnostics.PersistenceTagKeys.AggregateType, entityTypeName));

            return new ForbiddenException(
                TenantIsolationErrors.Build(entityTypeName, "written outside the current tenant for"),
                concurrencyEx);
        }

        return null;
    }
}
