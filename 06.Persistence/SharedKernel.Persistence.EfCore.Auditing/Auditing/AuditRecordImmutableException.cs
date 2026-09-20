using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// Thrown when an <c>UPDATE</c> or <c>DELETE</c> is attempted against an existing
/// <c>AuditRecord</c> row.
/// </summary>
/// <remarks>
/// The audit trail is append-only by construction — this
/// exception is the enforcement point, thrown by <see cref="AuditRecordImmutabilityInterceptor"/> (a
/// specific, tracked <see cref="AuditRecord"/> is known) or <see cref="AuditRecordMutationGuardInterceptor"/>
/// (a bulk <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>/raw-SQL statement is blocked before it runs, before
/// which row(s) it would have touched is even known). Should never occur against correctly-written
/// application code, since <c>IAuditTrailWriter</c> exposes no update/delete member in the first place;
/// this is a defense-in-depth guard against a caller reaching an <c>AuditRecord</c> row through some
/// other path.
/// </remarks>
public sealed class AuditRecordImmutableException : SharedKernelException
{
    /// <summary>
    /// Initialises a new <see cref="AuditRecordImmutableException"/> for the specific record identified by
    /// <paramref name="recordId"/>.
    /// </summary>
    /// <param name="recordId">The <c>Id</c> of the <c>AuditRecord</c> that a mutation was attempted against.</param>
    public AuditRecordImmutableException(Guid recordId)
        : base(BuildMessage(recordId), BuildError(recordId))
    {
        RecordId = recordId;
    }

    /// <summary>
    /// Initialises a new <see cref="AuditRecordImmutableException"/> for a blocked statement that
    /// names no single record — a bulk <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> or raw SQL statement,
    /// rejected before it runs.
    /// </summary>
    /// <param name="message">A message describing the blocked statement.</param>
    public AuditRecordImmutableException(string message)
        : base(message, Error.Conflict("persistence.audit_record_immutable", message))
    {
        RecordId = null;
    }

    /// <summary>
    /// Gets the <c>Id</c> of the specific <c>AuditRecord</c> a mutation was attempted against, or
    /// <see langword="null"/> when the blocked statement named no single record (a bulk/raw-SQL
    /// statement rejected before execution).
    /// </summary>
    public Guid? RecordId { get; }

    private static string BuildMessage(Guid recordId) =>
        $"AuditRecord '{recordId}' is immutable. The audit trail is append-only — update and " +
        "delete are structurally forbidden. Record a new AuditRecord via IAuditTrailWriter instead.";

    private static Error BuildError(Guid recordId) =>
        Error.Conflict("persistence.audit_record_immutable", BuildMessage(recordId));
}
