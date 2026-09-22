using Npgsql;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.Npgsql.Errors;

/// <summary>
/// Translates a <see cref="PostgresException"/>'s SQLSTATE into the platform's <see cref="Error"/> vocabulary.
/// Provider-level and EF-Core-free, so EF Core (<c>SharedKernel.Persistence.EfCore</c>, which applies it to
/// every failed <c>SaveChanges</c>) and Dapper callers classify database failures identically.
/// </summary>
/// <remarks>
/// <para><strong>Mapping, and why:</strong></para>
/// <list type="table">
/// <listheader><term>SQLSTATE</term><description>Result</description></listheader>
/// <item><term><c>23505</c> unique violation</term><description><see cref="ErrorType.Conflict"/>: the row conflicts with what is already stored.</description></item>
/// <item><term><c>23503</c> foreign key, <see cref="ForeignKeyViolationKind.MissingReference"/></term><description><see cref="ErrorType.Validation"/>: the caller referenced a row that does not exist and can fix the input.</description></item>
/// <item><term><c>23503</c> foreign key, <see cref="ForeignKeyViolationKind.ReferencedByDependent"/></term><description><see cref="ErrorType.Conflict"/>: another row still references this one; the same delete succeeds once it is gone.</description></item>
/// <item><term><c>23503</c> foreign key, <see cref="ForeignKeyViolationKind.Unknown"/></term><description><see cref="ErrorType.Validation"/> with a generic code.</description></item>
/// <item><term><c>23502</c>, <c>23514</c>, <c>23P01</c>, <c>22001</c></term><description><see cref="ErrorType.Validation"/>: the written values break a column or table rule.</description></item>
/// <item><term><c>40001</c>, <c>40P01</c></term><description><see cref="ErrorType.Conflict"/>, transient: the transaction lost to a concurrent one and can be retried unchanged.</description></item>
/// <item><term><c>55P03</c>, <c>57014</c></term><description><see cref="ErrorType.Conflict"/>, transient. The platform has no timeout or unavailable error type; a lock or statement timeout means the statement lost to concurrent work or load, which is what Conflict (HTTP 409, "retry may succeed") expresses best. A request cancelled by its own <see cref="CancellationToken"/> surfaces as <see cref="OperationCanceledException"/> instead and never reaches this classifier.</description></item>
/// <item><term><c>42501</c></term><description><see cref="ErrorType.Forbidden"/>: the database role is not allowed to do this, including a row rejected by a row-level security policy.</description></item>
/// </list>
/// <para>
/// Any other SQLSTATE returns <see langword="null"/>: the caller keeps the original exception. Every
/// <see cref="Error.Message"/> is a fixed, caller-safe sentence — never <see cref="PostgresException.MessageText"/>
/// or <see cref="PostgresException.Detail"/>, which can echo the offending value or internal names.
/// </para>
/// </remarks>
public static class PostgresExceptionClassifier
{
    /// <summary>Classifies <paramref name="exception"/>.</summary>
    /// <param name="exception">The PostgreSQL error.</param>
    /// <param name="foreignKeyViolationKind">
    /// For a <c>23503</c>, which side of the reference failed, when the caller knows (it knows whether it was
    /// inserting or deleting). Ignored for every other SQLSTATE.
    /// </param>
    /// <returns>The classification, or <see langword="null"/> when the SQLSTATE is not one this classifier maps.</returns>
    public static PostgresErrorClassification? Classify(
        PostgresException exception,
        ForeignKeyViolationKind foreignKeyViolationKind = ForeignKeyViolationKind.Unknown)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var sqlState = exception.SqlState;
        Error? error = sqlState switch
        {
            PostgresErrorCodes.UniqueViolation => Error.Conflict(
                PostgresClassifiedErrorCodes.UniqueViolation,
                "A row with the same unique key already exists."),

            PostgresErrorCodes.ForeignKeyViolation => ForeignKeyError(foreignKeyViolationKind),

            PostgresErrorCodes.NotNullViolation => Error.Validation(
                PostgresClassifiedErrorCodes.NotNullViolation,
                "A required value is missing."),

            PostgresErrorCodes.CheckViolation => Error.Validation(
                PostgresClassifiedErrorCodes.CheckViolation,
                "A value does not satisfy a rule of the stored data."),

            PostgresErrorCodes.ExclusionViolation => Error.Validation(
                PostgresClassifiedErrorCodes.ExclusionViolation,
                "The row overlaps an existing row that it may not overlap."),

            PostgresErrorCodes.StringDataRightTruncation => Error.Validation(
                PostgresClassifiedErrorCodes.ValueTooLong,
                "A value is longer than allowed."),

            PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected => Error.Conflict(
                PostgresClassifiedErrorCodes.TransientConflict,
                "The operation could not complete due to a transient transaction conflict. Retry it."),

            PostgresErrorCodes.LockNotAvailable => Error.Conflict(
                PostgresClassifiedErrorCodes.LockTimeout,
                "The data is locked by another operation. Retry it."),

            PostgresErrorCodes.QueryCanceled => Error.Conflict(
                PostgresClassifiedErrorCodes.StatementTimeout,
                "The operation took too long and was cancelled. Retry it."),

            PostgresErrorCodes.InsufficientPrivilege => Error.Forbidden(
                PostgresClassifiedErrorCodes.InsufficientPrivilege,
                "The operation is not permitted."),

            _ => null,
        };

        if (error is null)
            return null;

        var isTransient = exception.IsTransient
            || sqlState is PostgresErrorCodes.SerializationFailure
                or PostgresErrorCodes.DeadlockDetected
                or PostgresErrorCodes.LockNotAvailable
                or PostgresErrorCodes.QueryCanceled;

        return new PostgresErrorClassification(
            error,
            sqlState,
            isTransient,
            exception.ConstraintName,
            exception.TableName);
    }

    private static Error ForeignKeyError(ForeignKeyViolationKind kind) => kind switch
    {
        ForeignKeyViolationKind.MissingReference => Error.Validation(
            PostgresClassifiedErrorCodes.ForeignKeyReferenceMissing,
            "The operation references a row that does not exist."),

        ForeignKeyViolationKind.ReferencedByDependent => Error.Conflict(
            PostgresClassifiedErrorCodes.ForeignKeyDependentExists,
            "The row cannot be deleted because another row still references it."),

        _ => Error.Validation(
            PostgresClassifiedErrorCodes.ForeignKeyViolation,
            "The operation violates a reference between rows."),
    };
}
