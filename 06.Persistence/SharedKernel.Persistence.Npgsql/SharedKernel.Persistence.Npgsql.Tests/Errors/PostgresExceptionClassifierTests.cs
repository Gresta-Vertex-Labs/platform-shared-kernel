using FluentAssertions;
using Npgsql;
using SharedKernel.Persistence.Npgsql.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.Npgsql.Tests.Errors;

/// <summary>
/// The full SQLSTATE map of <see cref="PostgresExceptionClassifier"/> (P-558, finding A22). Pure: no container.
/// </summary>
public sealed class PostgresExceptionClassifierTests
{
    private static PostgresException Exception(string sqlState, string? constraint = null, string? table = null) =>
        new(
            messageText: "server text that may echo the value 'secret'",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState,
            detail: "Key (email)=(secret@example.com) already exists.",
            tableName: table,
            constraintName: constraint);

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, ErrorType.Conflict, PostgresClassifiedErrorCodes.UniqueViolation, false)]
    [InlineData(PostgresErrorCodes.NotNullViolation, ErrorType.Validation, PostgresClassifiedErrorCodes.NotNullViolation, false)]
    [InlineData(PostgresErrorCodes.CheckViolation, ErrorType.Validation, PostgresClassifiedErrorCodes.CheckViolation, false)]
    [InlineData(PostgresErrorCodes.ExclusionViolation, ErrorType.Validation, PostgresClassifiedErrorCodes.ExclusionViolation, false)]
    [InlineData(PostgresErrorCodes.StringDataRightTruncation, ErrorType.Validation, PostgresClassifiedErrorCodes.ValueTooLong, false)]
    [InlineData(PostgresErrorCodes.SerializationFailure, ErrorType.Conflict, PostgresClassifiedErrorCodes.TransientConflict, true)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, ErrorType.Conflict, PostgresClassifiedErrorCodes.TransientConflict, true)]
    [InlineData(PostgresErrorCodes.LockNotAvailable, ErrorType.Conflict, PostgresClassifiedErrorCodes.LockTimeout, true)]
    [InlineData(PostgresErrorCodes.QueryCanceled, ErrorType.Conflict, PostgresClassifiedErrorCodes.StatementTimeout, true)]
    [InlineData(PostgresErrorCodes.InsufficientPrivilege, ErrorType.Forbidden, PostgresClassifiedErrorCodes.InsufficientPrivilege, false)]
    public void Classify_MapsSqlState(string sqlState, ErrorType type, string code, bool transient)
    {
        var classification = PostgresExceptionClassifier.Classify(Exception(sqlState));

        classification.Should().NotBeNull();
        classification!.Error.Type.Should().Be(type);
        classification.Error.Code.Should().Be(code);
        classification.SqlState.Should().Be(sqlState);
        classification.IsTransient.Should().Be(transient);
    }

    [Theory]
    [InlineData(ForeignKeyViolationKind.MissingReference, ErrorType.Validation, PostgresClassifiedErrorCodes.ForeignKeyReferenceMissing)]
    [InlineData(ForeignKeyViolationKind.ReferencedByDependent, ErrorType.Conflict, PostgresClassifiedErrorCodes.ForeignKeyDependentExists)]
    [InlineData(ForeignKeyViolationKind.Unknown, ErrorType.Validation, PostgresClassifiedErrorCodes.ForeignKeyViolation)]
    public void Classify_ForeignKey_UsesTheCallersKind(ForeignKeyViolationKind kind, ErrorType type, string code)
    {
        var classification = PostgresExceptionClassifier.Classify(Exception(PostgresErrorCodes.ForeignKeyViolation), kind);

        classification!.Error.Type.Should().Be(type);
        classification.Error.Code.Should().Be(code);
    }

    [Fact]
    public void Classify_UnmappedSqlState_ReturnsNull()
    {
        PostgresExceptionClassifier.Classify(Exception(PostgresErrorCodes.SyntaxError)).Should().BeNull();
    }

    [Fact]
    public void Classify_NeverEchoesTheServerMessageOrDetail()
    {
        var classification = PostgresExceptionClassifier.Classify(Exception(PostgresErrorCodes.UniqueViolation));

        classification!.Error.Message.Should().NotContain("secret");
    }

    [Fact]
    public void Classify_CarriesConstraintAndTableForDiagnostics()
    {
        var classification = PostgresExceptionClassifier.Classify(
            Exception(PostgresErrorCodes.UniqueViolation, constraint: "ix_users_email", table: "users"));

        classification!.ConstraintName.Should().Be("ix_users_email");
        classification.TableName.Should().Be("users");
    }
}
