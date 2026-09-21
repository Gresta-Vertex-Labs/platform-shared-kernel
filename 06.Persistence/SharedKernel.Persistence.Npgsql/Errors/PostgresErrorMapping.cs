using Npgsql;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Persistence.Npgsql.Errors;

/// <summary>
/// Runs a database operation and turns a PostgreSQL error the platform classifies (unique or foreign-key
/// violation, serialization failure, row-level security rejection, ...) into a failed
/// <see cref="Result"/>, with the same <see cref="Primitives.Errors.Error"/> EF Core's <c>SaveChanges</c>
/// classification produces.
/// </summary>
/// <remarks>
/// For code that talks to the database without EF Core (Dapper, raw ADO.NET). A
/// <see cref="PostgresException"/> the classifier does not map, and every other exception, propagates
/// unchanged.
/// <code>
/// var result = await PostgresErrorMapping.TryAsync(() =&gt;
///     session.Connection.ExecuteAsync(session.Command(InsertSql, order, ct)));
/// </code>
/// </remarks>
public static class PostgresErrorMapping
{
    /// <summary>Runs <paramref name="operation"/>, mapping classified PostgreSQL errors to a failure.</summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="operation">The database operation.</param>
    /// <param name="foreignKeyViolationKind">
    /// Which side of a foreign key a <c>23503</c> concerns, when the caller knows (inserting a reference,
    /// or deleting a referenced row).
    /// </param>
    /// <returns>The operation's value, or the classified error.</returns>
    public static async Task<Result<T>> TryAsync<T>(
        Func<Task<T>> operation,
        ForeignKeyViolationKind foreignKeyViolationKind)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            return Result<T>.Success(await operation().ConfigureAwait(false));
        }
        catch (PostgresException exception)
            when (PostgresExceptionClassifier.Classify(exception, foreignKeyViolationKind) is { } classification)
        {
            return Result<T>.Failure(classification.Error);
        }
    }

    /// <summary>Runs <paramref name="operation"/>, mapping classified PostgreSQL errors to a failure.</summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="operation">The database operation.</param>
    /// <returns>The operation's value, or the classified error.</returns>
    public static Task<Result<T>> TryAsync<T>(Func<Task<T>> operation) =>
        TryAsync(operation, ForeignKeyViolationKind.Unknown);

    /// <summary>Runs <paramref name="operation"/>, mapping classified PostgreSQL errors to a failure.</summary>
    /// <param name="operation">The database operation.</param>
    /// <param name="foreignKeyViolationKind">See <see cref="TryAsync{T}(Func{Task{T}}, ForeignKeyViolationKind)"/>.</param>
    /// <returns>Success, or the classified error.</returns>
    public static async Task<Result> TryAsync(
        Func<Task> operation,
        ForeignKeyViolationKind foreignKeyViolationKind)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            await operation().ConfigureAwait(false);
            return Result.Success();
        }
        catch (PostgresException exception)
            when (PostgresExceptionClassifier.Classify(exception, foreignKeyViolationKind) is { } classification)
        {
            return Result.Failure(classification.Error);
        }
    }

    /// <summary>Runs <paramref name="operation"/>, mapping classified PostgreSQL errors to a failure.</summary>
    /// <param name="operation">The database operation.</param>
    /// <returns>Success, or the classified error.</returns>
    public static Task<Result> TryAsync(Func<Task> operation) =>
        TryAsync(operation, ForeignKeyViolationKind.Unknown);
}
