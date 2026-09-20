namespace SharedKernel.Persistence.PostgreSQL.Exceptions;

/// <summary>
/// <c>Error.Code</c> constants used by <see cref="PostgreSqlDbUpdateExceptionClassifier"/>.
/// </summary>
internal static class PostgreSqlErrorCodes
{
    /// <summary>A PostgreSQL <c>23505</c> unique-constraint violation.</summary>
    public const string UniqueViolation = "persistence.postgresql.unique_violation";

    /// <summary>
    /// A PostgreSQL <c>23503</c> foreign-key violation on an insert/update that references a
    /// non-existent parent row.
    /// </summary>
    public const string ForeignKeyReferenceMissing = "persistence.postgresql.foreign_key_reference_missing";

    /// <summary>
    /// A PostgreSQL <c>23503</c> foreign-key violation on a delete blocked by an existing dependent
    /// row.
    /// </summary>
    public const string ForeignKeyDependentExists = "persistence.postgresql.foreign_key_dependent_exists";

    /// <summary>
    /// A PostgreSQL <c>40001</c> serialization failure or <c>40P01</c> deadlock — both transient and
    /// safe to retry.
    /// </summary>
    public const string TransientConflict = "persistence.postgresql.transient_conflict";
}
