using Microsoft.EntityFrameworkCore;

namespace SharedKernel.Persistence.EfCore.Extensibility;

/// <summary>
/// Translates a provider-specific <see cref="DbUpdateException"/> (a unique-constraint violation, a
/// foreign-key violation, a serialization failure,...) into one of this platform's typed
/// <c>SharedKernel.Core.Exceptions</c> carrying the matching <c>Error</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Context.SharedKernelDbContext"/> already translates
/// <see cref="DbUpdateConcurrencyException"/> for <c>IHasConcurrency</c> entities via
/// the context itself — that translation is unconditional and provider-neutral (a
/// concurrency token mismatch means the same thing on every relational provider). A unique-index or
/// foreign-key violation, by contrast, is signalled differently per provider (PostgreSQL's SQLSTATE
/// <c>23505</c>/<c>23503</c>, SQL Server's error numbers,...), so
/// <c>SharedKernel.Persistence.EfCore</c> — which must never reference a specific ADO.NET provider —
/// cannot classify it directly. This interface is the seam: a provider package (e.g.
/// <c>SharedKernel.Persistence.EfCore</c>'s own PostgreSQL classifier, always registered first) implements it, and
/// <see cref="Context.SharedKernelDbContext"/> consults every registered classifier, in registration
/// order, immediately after the concurrency translation fails to match.
/// </para>
/// <para>
/// Matches <c>05.Application</c>'s "expected failures are <c>Result</c> values, never exceptions"
/// contract at the persistence boundary: a unique/FK violation becomes a typed exception carrying
/// <c>Error.Conflict(...)</c> or <c>Error.Validation(...)</c>, which
/// <c>SharedKernel.Application</c>'s exception-to-<c>Result</c> boundary (or a service's own
/// <c>try/catch</c>) can turn into a <c>Result</c> instead of an unhandled 500.
/// </para>
/// </remarks>
internal interface IDbUpdateExceptionClassifier
{
    /// <summary>
    /// Attempts to classify <paramref name="exception"/>.
    /// </summary>
    /// <param name="exception">
    /// The exception observed at the <c>SaveChanges</c>/<c>SaveChangesAsync</c> call boundary. Never
    /// a <see cref="DbUpdateConcurrencyException"/> — those are exclusively
    /// the context's own concern and are never offered to a classifier.
    /// </param>
    /// <returns>
    /// A typed <c>SharedKernel.Core.Exceptions</c> exception to throw instead of
    /// <paramref name="exception"/>, or <see langword="null"/> when this classifier does not
    /// recognize the failure — the caller tries the next registered classifier, if any, and
    /// otherwise lets <paramref name="exception"/> propagate unchanged.
    /// </returns>
    Exception? TryClassify(DbUpdateException exception);
}
