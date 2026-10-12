namespace SharedKernel.Persistence.Abstractions.Errors;

/// <summary>The stable, lowercase error codes of the persistence packages that more than one package raises.</summary>
public static class PersistenceErrorCodes
{
    /// <summary>
    /// An operation that needs a tenant ran without one (<c>ErrorType.Forbidden</c>). Raised identically by the
    /// Dapper sessions, the audit services and the EF Core tenant write guard.
    /// </summary>
    public const string TenantRequired = "persistence.tenant_required";

    /// <summary>
    /// A record or chain of another tenant was addressed (<c>ErrorType.Forbidden</c>). Raised by the EF Core tenant
    /// write guard and the audit services.
    /// </summary>
    public const string TenantIsolationViolation = "persistence.tenant_isolation_violation";

    /// <summary>
    /// An audit record does not exist, or belongs to another tenant (<c>ErrorType.NotFound</c>). One code and one
    /// message for both cases, so the answer is no existence oracle.
    /// </summary>
    public const string AuditRecordNotFound = "persistence.audit_record_not_found";
}
