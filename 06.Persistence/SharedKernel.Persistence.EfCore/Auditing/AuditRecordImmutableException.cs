using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// Thrown when an <c>UPDATE</c> or <c>DELETE</c> is attempted against an existing
/// <c>AuditRecord</c> row.
/// </summary>
/// <remarks>
/// WO-071/P-457/D-123. The audit trail is append-only by construction — this exception is the
/// enforcement point, thrown by <see cref="AuditRecordImmutabilityInterceptor"/>. Should never occur
/// against correctly-written application code, since <c>IAuditTrailWriter</c> exposes no
/// update/delete member in the first place; this is a defense-in-depth guard against a caller
/// reaching an <c>AuditRecord</c> entry through some other path (e.g. a hand-rolled
/// <c>DbContext.Update(...)</c>/<c>Remove(...)</c> call).
/// </remarks>
public sealed class AuditRecordImmutableException : SharedKernelException
{
    /// <summary>
    /// Initialises a new <see cref="AuditRecordImmutableException"/> for the record identified by
    /// <paramref name="recordId"/>.
    /// </summary>
    /// <param name="recordId">The <c>Id</c> of the <c>AuditRecord</c> that a mutation was attempted against.</param>
    public AuditRecordImmutableException(Guid recordId)
        : base(BuildMessage(recordId), BuildError(recordId))
    {
        RecordId = recordId;
    }

    /// <summary>Gets the <c>Id</c> of the <c>AuditRecord</c> that a mutation was attempted against.</summary>
    public Guid RecordId { get; }

    private static string BuildMessage(Guid recordId) =>
        $"AuditRecord '{recordId}' is immutable. The audit trail is append-only — update and " +
        "delete are structurally forbidden. Record a new AuditRecord via IAuditTrailWriter instead.";

    private static Error BuildError(Guid recordId) =>
        Error.Conflict("persistence.audit_record_immutable", BuildMessage(recordId));
}
