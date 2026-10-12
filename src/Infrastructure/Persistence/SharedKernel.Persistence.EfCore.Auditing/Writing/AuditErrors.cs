using SharedKernel.Persistence.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Auditing.Writing;

/// <summary>The classified errors of the audit services. Fixed, client-safe messages: no tenant, record id or key.</summary>
internal static class AuditErrors
{
    /// <summary>
    /// The audit record does not exist, or belongs to another tenant. One error for both cases, so the answer is no
    /// existence oracle.
    /// </summary>
    public static Error RecordNotFound() =>
        Error.NotFound(PersistenceErrorCodes.AuditRecordNotFound, "The audit record does not exist.");

    /// <summary>The caller addressed the audit chains of another tenant.</summary>
    public static Error OtherTenantChain() =>
        Error.Forbidden(
            PersistenceErrorCodes.TenantIsolationViolation,
            "The audit chains of another tenant are not accessible to the caller. Enter a cross-tenant scope to do this.");
}
