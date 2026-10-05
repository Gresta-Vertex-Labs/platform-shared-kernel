namespace SharedKernel.Persistence.Npgsql.Errors;

/// <summary>
/// Which side of a foreign key a <c>23503</c> violation came from. PostgreSQL reports both cases with the same
/// SQLSTATE (and the referencing table in <c>TableName</c> either way), so only the caller, who knows what it
/// was writing, can tell them apart.
/// </summary>
public enum ForeignKeyViolationKind
{
    /// <summary>The caller does not know. Classified as a generic validation failure.</summary>
    Unknown = 0,

    /// <summary>An insert or update referenced a parent row that does not exist (a validation failure).</summary>
    MissingReference = 1,

    /// <summary>A delete or key update was blocked by a row that still references it (a conflict).</summary>
    ReferencedByDependent = 2,
}
