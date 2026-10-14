namespace SharedKernel.Persistence.Abstractions.Errors;

/// <summary>
/// Every stable error code the persistence packages raise: lowercase <c>persistence.{reason}</c> or
/// <c>persistence.{area}.{reason}</c>, declared once here and read by every package's error factory and exception.
/// </summary>
/// <remarks>
/// Each constant's remarks name the <c>ErrorType</c> it answers. A package builds the <c>Error</c> and the exception
/// itself; only the string lives here.
/// </remarks>
public static class PersistenceErrorCodes
{
    /// <summary>Tenant isolation: no tenant and a row or chain of another tenant.</summary>
    public static class Tenancy
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
    }

    /// <summary>Optimistic concurrency.</summary>
    public static class Concurrency
    {
        /// <summary>
        /// A save met a stale, foreign or unknown-key version, or a row of another tenant (<c>ErrorType.Conflict</c>).
        /// The presentation layer answers 412 on a conditional request and 409 otherwise.
        /// </summary>
        public const string Conflict = "persistence.concurrency_conflict";
    }

    /// <summary>PostgreSQL SQLSTATE classification (<c>SharedKernel.Persistence.Npgsql</c>).</summary>
    public static class Postgresql
    {
        /// <summary>23505 (<c>ErrorType.Conflict</c>).</summary>
        public const string UniqueViolation = "persistence.postgresql.unique_violation";

        /// <summary>23503 on a missing referenced row (<c>ErrorType.Validation</c>).</summary>
        public const string ForeignKeyReferenceMissing = "persistence.postgresql.foreign_key_reference_missing";

        /// <summary>23503 on a row that dependents still reference (<c>ErrorType.Conflict</c>).</summary>
        public const string ForeignKeyDependentExists = "persistence.postgresql.foreign_key_dependent_exists";

        /// <summary>23503 that could not be told apart (<c>ErrorType.Validation</c>).</summary>
        public const string ForeignKeyViolation = "persistence.postgresql.foreign_key_violation";

        /// <summary>23502 (<c>ErrorType.Validation</c>).</summary>
        public const string NotNullViolation = "persistence.postgresql.not_null_violation";

        /// <summary>23514 (<c>ErrorType.Validation</c>).</summary>
        public const string CheckViolation = "persistence.postgresql.check_violation";

        /// <summary>23P01 (<c>ErrorType.Validation</c>).</summary>
        public const string ExclusionViolation = "persistence.postgresql.exclusion_violation";

        /// <summary>22001 (<c>ErrorType.Validation</c>).</summary>
        public const string ValueTooLong = "persistence.postgresql.value_too_long";

        /// <summary>40001 and 40P01 (<c>ErrorType.Conflict</c>, transient).</summary>
        public const string TransientConflict = "persistence.postgresql.transient_conflict";

        /// <summary>55P03 (<c>ErrorType.Conflict</c>; it also covers <c>NOWAIT</c> and <c>SKIP LOCKED</c> contention).</summary>
        public const string LockTimeout = "persistence.postgresql.lock_timeout";

        /// <summary>57014, a <c>statement_timeout</c> expiry (<c>ErrorType.Timeout</c>).</summary>
        public const string StatementTimeout = "persistence.postgresql.statement_timeout";

        /// <summary>42501, including a row-level security <c>WITH CHECK</c> rejection (<c>ErrorType.Forbidden</c>).</summary>
        public const string InsufficientPrivilege = "persistence.postgresql.insufficient_privilege";
    }

    /// <summary>The audit ledger.</summary>
    public static class Audit
    {
        /// <summary>
        /// An audit record does not exist, or belongs to another tenant (<c>ErrorType.NotFound</c>). One code and one
        /// message for both cases, so the answer is no existence oracle.
        /// </summary>
        public const string RecordNotFound = "persistence.audit_record_not_found";

        /// <summary>An audit chain failed verification (<c>ErrorType.Conflict</c>).</summary>
        public const string ChainNotIntact = "persistence.audit_chain_not_intact";
    }

    /// <summary>Field encryption.</summary>
    public static class Encryption
    {
        /// <summary>A stored encrypted value is not a valid encrypted payload (<c>ErrorType.Unexpected</c>).</summary>
        public const string MalformedPayload = "persistence.encryption.malformed_payload";

        /// <summary>A stored encrypted value could not be decrypted (<c>ErrorType.Unexpected</c>).</summary>
        public const string DecryptionFailed = "persistence.encryption.decryption_failed";

        /// <summary>A tenant data key is not valid (<c>ErrorType.Unexpected</c>).</summary>
        public const string DataKeyInvalid = "persistence.encryption.data_key_invalid";

        /// <summary>
        /// A tenant data key could not be unwrapped by the key service (<c>ErrorType.Unavailable</c>,
        /// <c>Timeout</c> or <c>Unexpected</c>, following the key service's failure).
        /// </summary>
        public const string DataKeyUnwrapFailed = "persistence.encryption.data_key_unwrap_failed";

        /// <summary>A key id the key source does not know (<c>ErrorType.Unexpected</c>).</summary>
        public const string KeyNotFound = "persistence.encryption.key_not_found";

        /// <summary>A shredded tenant's data was read or written (<c>ErrorType.NotFound</c>).</summary>
        public const string TenantKeyShredded = "persistence.encryption.tenant_key_shredded";

        /// <summary>A tenant shred left values that are not erased yet (<c>ErrorType.Conflict</c>).</summary>
        public const string TenantShredIncomplete = "persistence.encryption.tenant_shred_incomplete";
    }

    /// <summary>Set-based bulk mutations.</summary>
    public static class BulkMutation
    {
        /// <summary>
        /// A bulk statement was given a specification it cannot translate to one statement (<c>ErrorType.Validation</c>;
        /// <c>not_supported</c> is the platform's reason word for an unsupported input).
        /// </summary>
        public const string SpecificationNotSupported = "persistence.bulk_mutation.specification_not_supported";
    }
}
