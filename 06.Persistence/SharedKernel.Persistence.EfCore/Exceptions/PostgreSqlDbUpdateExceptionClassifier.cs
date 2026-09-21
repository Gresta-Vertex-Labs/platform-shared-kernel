using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.Npgsql.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Exceptions;

/// <summary>
/// Classifies a failed <c>SaveChanges</c> by its PostgreSQL SQLSTATE through
/// <see cref="PostgresExceptionClassifier"/> and throws the matching typed exception, keeping the
/// <see cref="DbUpdateException"/> as the inner exception.
/// </summary>
/// <remarks>
/// <para>
/// Always registered by <c>AddSharedKernelEfCore&lt;TContext&gt;(...).Build()</c>, first in the classifier
/// list; there is no registration path without it.
/// </para>
/// <para>
/// A <c>23503</c> foreign-key violation is resolved to one side of the reference from the failing entries,
/// not from "any entry is being deleted": PostgreSQL reports the <em>referencing</em> table in
/// <see cref="PostgresException.TableName"/> in both directions, so an added or modified entry mapped to that
/// table means the new reference is missing (Validation), and otherwise a deleted entry means a dependent row
/// still exists (Conflict). A mixed batch that inserts a child and deletes an unrelated parent is therefore
/// classified by the entry that actually failed.
/// </para>
/// <para>
/// The violated constraint's name is logged (EventId <c>6010</c>) rather than returned: it names internal schema
/// objects, and <see cref="Error"/> has no metadata bag to carry it.
/// </para>
/// </remarks>
internal sealed class PostgreSqlDbUpdateExceptionClassifier : IDbUpdateExceptionClassifier
{
    private readonly ILogger<PostgreSqlDbUpdateExceptionClassifier> _logger;

    /// <summary>Initialises the classifier.</summary>
    /// <param name="logger">Logger for the classified constraint; <see cref="NullLogger{T}"/> when omitted.</param>
    public PostgreSqlDbUpdateExceptionClassifier(ILogger<PostgreSqlDbUpdateExceptionClassifier>? logger = null)
    {
        _logger = logger ?? NullLogger<PostgreSqlDbUpdateExceptionClassifier>.Instance;
    }

    /// <inheritdoc />
    public Exception? TryClassify(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (FindPostgresException(exception) is not { } postgresException)
            return null;

        var foreignKeyKind = postgresException.SqlState == PostgresErrorCodes.ForeignKeyViolation
            ? ResolveForeignKeyKind(exception.Entries, postgresException)
            : ForeignKeyViolationKind.Unknown;

        if (PostgresExceptionClassifier.Classify(postgresException, foreignKeyKind) is not { } classification)
            return null;

        PersistenceLog.DatabaseErrorClassified(
            _logger,
            classification.SqlState,
            classification.Error.Code,
            classification.ConstraintName ?? string.Empty,
            classification.TableName ?? string.Empty);

        return classification.Error.Type switch
        {
            ErrorType.Validation => new ValidationException(classification.Error, exception),
            ErrorType.Forbidden => new ForbiddenException(classification.Error, exception),
            _ => new ConflictException(classification.Error, exception),
        };
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
                return postgresException;
        }

        return null;
    }

    internal static ForeignKeyViolationKind ResolveForeignKeyKind(
        IReadOnlyList<EntityEntry> entries,
        PostgresException postgresException)
    {
        var sawDeleted = false;
        var sawWrite = false;

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added or EntityState.Modified:
                    sawWrite = true;
                    if (MapsToTable(entry.Metadata, postgresException.TableName, postgresException.SchemaName))
                        return ForeignKeyViolationKind.MissingReference;
                    break;

                case EntityState.Deleted:
                    sawDeleted = true;
                    break;
            }
        }

        if (sawDeleted)
            return ForeignKeyViolationKind.ReferencedByDependent;

        return sawWrite ? ForeignKeyViolationKind.MissingReference : ForeignKeyViolationKind.Unknown;
    }

    private static bool MapsToTable(IEntityType entityType, string? tableName, string? schemaName)
    {
        if (string.IsNullOrEmpty(tableName))
            return false;

        foreach (var mapping in entityType.GetTableMappings())
        {
            var table = mapping.Table;
            if (!string.Equals(table.Name, tableName, StringComparison.Ordinal))
                continue;

            // PostgreSQL always reports the schema; EF Core leaves it null for the default search path.
            if (table.Schema is null || string.IsNullOrEmpty(schemaName)
                || string.Equals(table.Schema, schemaName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
