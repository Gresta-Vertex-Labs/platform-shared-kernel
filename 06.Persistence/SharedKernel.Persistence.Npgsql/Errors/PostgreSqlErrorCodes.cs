namespace SharedKernel.Persistence.Npgsql.Errors;

/// <summary>
/// <c>Error.Code</c> values produced by <see cref="PostgresExceptionClassifier"/>. Stable wire values: a
/// client or dashboard may match on them.
/// </summary>
/// <remarks>
/// These are the platform's error codes, not PostgreSQL SQLSTATEs — for the SQLSTATE values themselves use
/// Npgsql's <see cref="global::Npgsql.PostgresErrorCodes"/>.
/// </remarks>
public static class PostgreSqlErrorCodes
{
    /// <summary><c>23505</c>: a row with the same unique key already exists.</summary>
    public const string UniqueViolation = "persistence.postgresql.unique_violation";

    /// <summary><c>23503</c> on an insert or update: the referenced parent row does not exist.</summary>
    public const string ForeignKeyReferenceMissing = "persistence.postgresql.foreign_key_reference_missing";

    /// <summary><c>23503</c> on a delete or key update: another row still references this one.</summary>
    public const string ForeignKeyDependentExists = "persistence.postgresql.foreign_key_dependent_exists";

    /// <summary><c>23503</c> when the caller could not say which side of the reference failed.</summary>
    public const string ForeignKeyViolation = "persistence.postgresql.foreign_key_violation";

    /// <summary><c>23502</c>: a required column was given no value.</summary>
    public const string NotNullViolation = "persistence.postgresql.not_null_violation";

    /// <summary><c>23514</c>: a value failed a check constraint.</summary>
    public const string CheckViolation = "persistence.postgresql.check_violation";

    /// <summary><c>23P01</c>: a row overlaps an existing row under an exclusion constraint.</summary>
    public const string ExclusionViolation = "persistence.postgresql.exclusion_violation";

    /// <summary><c>22001</c>: a value is longer than its column allows.</summary>
    public const string ValueTooLong = "persistence.postgresql.value_too_long";

    /// <summary><c>40001</c> (serialization failure) or <c>40P01</c> (deadlock): retry the operation unchanged.</summary>
    public const string TransientConflict = "persistence.postgresql.transient_conflict";

    /// <summary><c>55P03</c>: a lock could not be acquired within <c>lock_timeout</c>.</summary>
    public const string LockTimeout = "persistence.postgresql.lock_timeout";

    /// <summary><c>57014</c>: the statement was cancelled, typically by <c>statement_timeout</c>.</summary>
    public const string StatementTimeout = "persistence.postgresql.statement_timeout";

    /// <summary>
    /// <c>42501</c>: the database role may not perform the operation. Also raised when a row-level security
    /// policy rejects a written row.
    /// </summary>
    public const string InsufficientPrivilege = "persistence.postgresql.insufficient_privilege";
}
