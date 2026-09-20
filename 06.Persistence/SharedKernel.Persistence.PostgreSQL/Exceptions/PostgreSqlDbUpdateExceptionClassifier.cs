using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.PostgreSQL.Exceptions;

/// <summary>
/// PostgreSQL implementation of <see cref="IDbUpdateExceptionClassifier"/>, translating a
/// <see cref="PostgresException"/>'s SQLSTATE code into one of this platform's typed
/// <c>SharedKernel.Core.Exceptions</c>.
/// </summary>
/// <remarks>
/// <para>
/// Registered by
/// <c>PostgreSQLPersistenceExtensions.AddSharedKernelPostgreSQL</c> — one singleton instance shared
/// across every <c>DbContext</c> in the process.
/// </para>
/// <para>
/// <strong>Mapping decisions, and why:</strong>
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>23505</c> (unique violation) → <see cref="ConflictException"/>. A duplicate key is a
/// state-based conflict with what already exists in the store, not a malformed request.
/// </description></item>
/// <item><description>
/// <c>23503</c> (foreign-key violation) on an <see cref="EntityState.Added"/>/
/// <see cref="EntityState.Modified"/> entry → <see cref="ValidationException"/>. The caller supplied
/// a reference to a row that does not exist — this is a malformed-input error the caller can fix by
/// supplying a valid reference, exactly like any other invalid-field error.
/// </description></item>
/// <item><description>
/// <c>23503</c> on an <see cref="EntityState.Deleted"/> entry → <see cref="ConflictException"/>. The
/// row being deleted is still referenced by a dependent row — this is a conflict with the store's
/// current state (another row's existence), not a malformed request; retrying the exact same delete
/// after removing the dependent succeeds.
/// </description></item>
/// <item><description>
/// <c>40001</c> (serialization failure) / <c>40P01</c> (deadlock detected) → <see cref="ConflictException"/>,
/// both transient and safe for the caller to retry unchanged.
/// </description></item>
/// </list>
/// <para>
/// Every message is a fixed, caller-facing string — never <see cref="PostgresException.Message"/> or
/// <see cref="PostgresException.Detail"/>, both of which can echo back the offending column value
/// (potentially sensitive data) or internal constraint/table names.
/// </para>
/// </remarks>
public sealed class PostgreSqlDbUpdateExceptionClassifier : IDbUpdateExceptionClassifier
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";
    private const string SerializationFailure = "40001";
    private const string DeadlockDetected = "40P01";

    /// <inheritdoc />
    public Exception? TryClassify(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.InnerException is not PostgresException postgresException)
            return null;

        return postgresException.SqlState switch
        {
            UniqueViolation => new ConflictException(
                Error.Conflict(
                    PostgreSqlErrorCodes.UniqueViolation,
                    "A row with the same unique key already exists."),
                exception),

            ForeignKeyViolation => ClassifyForeignKeyViolation(exception),

            SerializationFailure or DeadlockDetected => new ConflictException(
                Error.Conflict(
                    PostgreSqlErrorCodes.TransientConflict,
                    "The operation could not complete due to a transient transaction conflict. Retry it."),
                exception),

            _ => null,
        };
    }

    private static Exception ClassifyForeignKeyViolation(DbUpdateException exception)
    {
        bool isDelete = exception.Entries.Any(static (EntityEntry entry) => entry.State == EntityState.Deleted);

        return isDelete
            ? new ConflictException(
                Error.Conflict(
                    PostgreSqlErrorCodes.ForeignKeyDependentExists,
                    "The row cannot be deleted because another row still references it."),
                exception)
                    : new ValidationException(
                Error.Validation(
                    PostgreSqlErrorCodes.ForeignKeyReferenceMissing,
                    "The operation references a row that does not exist."));
    }
}
