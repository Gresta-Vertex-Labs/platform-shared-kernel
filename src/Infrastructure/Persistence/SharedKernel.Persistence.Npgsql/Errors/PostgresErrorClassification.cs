using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.Npgsql.Errors;

/// <summary>
/// The platform's reading of a <see cref="global::Npgsql.PostgresException"/>: a caller-safe
/// <see cref="Error"/> plus the diagnostic detail needed to log it.
/// </summary>
/// <remarks>
/// <see cref="Error"/> never carries the server's message or detail, both of which can echo the offending
/// value. <see cref="ConstraintName"/> and <see cref="TableName"/> are for logs and metrics, not for clients:
/// they name internal schema objects.
/// </remarks>
public sealed class PostgresErrorClassification
{
    internal PostgresErrorClassification(
        Error error,
        string sqlState,
        bool isTransient,
        string? constraintName,
        string? tableName)
    {
        Error = error;
        SqlState = sqlState;
        IsTransient = isTransient;
        ConstraintName = constraintName;
        TableName = tableName;
    }

    /// <summary>Gets the caller-safe error: its <see cref="ErrorType"/> and a <see cref="PostgresClassifiedErrorCodes"/> code.</summary>
    public Error Error { get; }

    /// <summary>Gets the PostgreSQL SQLSTATE that was classified.</summary>
    public string SqlState { get; }

    /// <summary>
    /// Gets whether retrying the whole operation unchanged can succeed (serialization failure, deadlock, lock
    /// or statement timeout, and whatever Npgsql itself reports as transient).
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>Gets the violated constraint's name, when the server reported one. Diagnostic only.</summary>
    public string? ConstraintName { get; }

    /// <summary>Gets the table the server reported, when there is one. Diagnostic only.</summary>
    public string? TableName { get; }
}
