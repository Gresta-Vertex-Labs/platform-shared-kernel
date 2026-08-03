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
/// entities implementing <see cref="IHasConcurrency"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>CORRECTED, WO-051/P-315 (D-66) — genuine defect found and fixed:</strong> this
/// interceptor originally performed the translation directly inside its
/// <see cref="SaveChangesFailed"/>/<see cref="SaveChangesFailedAsync"/> overrides, on the
/// assumption that an exception thrown from those hooks would replace the exception propagating
/// out of <c>DbContext.SaveChanges</c>/<c>SaveChangesAsync</c>. A real <see cref="IHasConcurrency"/>
/// SQLite test fixture (added by this same phase — see
/// <c>ConcurrencyInterceptorTests</c>) proved that assumption FALSE against EF Core 10.0.5, using
/// the platform's real three-interceptor (Audit/SoftDelete/Concurrency) registration shape:
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
/// actual ENFORCEMENT point moved to
/// <see cref="SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext.SaveChanges(bool)"/> and
/// <see cref="SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext.SaveChangesAsync(bool, CancellationToken)"/>,
/// which wrap the base call in a try/catch and call <see cref="TryTranslate"/> directly — the only
/// mechanism EF Core 10 actually honors for replacing a propagating <c>SaveChanges</c> exception.
/// <see cref="SaveChangesFailed"/>/<see cref="SaveChangesFailedAsync"/> are retained as harmless
/// base-delegating overrides for interceptor-pipeline symmetry (this class remains one of the
/// platform's three registered interceptors) — they perform no translation of their own.
/// </para>
/// <para>
/// <strong>No retry logic.</strong> Conflict resolution is the application layer's
/// responsibility — <see cref="TryTranslate"/> surfaces the conflict and stops. Callers that
/// require retry behaviour must implement it in a MediatR pipeline behavior or equivalent.
/// </para>
/// <para>
/// Non-concurrency exceptions and <see cref="DbUpdateConcurrencyException"/> instances that do not
/// involve <see cref="IHasConcurrency"/> entries are left untranslated by <see cref="TryTranslate"/>
/// (returns <see langword="null"/>) and propagate unchanged.
/// </para>
/// <para>
/// <strong>Structured logging (WO-053/P-333):</strong> <see cref="TryTranslate"/> is a
/// non-static instance method specifically so it can log via this interceptor's own injected
/// <see cref="ILogger{TCategoryName}"/> — a <c>ConcurrencyConflictDetected</c> Warning
/// (EventId <c>6000</c>) is emitted immediately before the translated
/// <see cref="ConflictException"/> is returned, naming only the conflicting entry's CLR type —
/// never the row payload. <see cref="Context.SharedKernelDbContext"/>'s catch-filter calls this
/// method via its own held <c>_concurrencyInterceptor</c> field (already constructor-injected, no
/// new plumbing), so both <c>SaveChanges</c> and <c>SaveChangesAsync</c> pick up the logging
/// automatically.
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
    /// entry.
    /// </summary>
    /// <param name="exception">The exception observed at the <c>SaveChanges</c> call boundary.</param>
    /// <returns>
    /// A <see cref="ConflictException"/> carrying <see cref="Error.Conflict(string, string)"/> and
    /// wrapping <paramref name="exception"/> as its inner exception when translation applies;
    /// otherwise <see langword="null"/> — the caller must let <paramref name="exception"/>
    /// propagate unchanged in that case.
    /// </returns>
    internal ConflictException? TryTranslate(Exception exception)
    {
        if (exception is not DbUpdateConcurrencyException concurrencyEx)
            return null;

        var conflictingEntry = concurrencyEx.Entries
            .FirstOrDefault(e => e.Entity is IHasConcurrency);

        if (conflictingEntry is null)
            return null;

        PersistenceLog.ConcurrencyConflictDetected(_logger, conflictingEntry.Entity.GetType().Name);

        return new ConflictException(
            Error.Conflict(
                "persistence.concurrency_conflict",
                "A concurrency conflict occurred. The entity was modified by another process. Reload and retry."),
            concurrencyEx);
    }
}
