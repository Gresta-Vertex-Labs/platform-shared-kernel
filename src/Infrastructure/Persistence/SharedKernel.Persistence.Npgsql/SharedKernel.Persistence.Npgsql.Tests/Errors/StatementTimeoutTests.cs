using FluentAssertions;
using Npgsql;
using SharedKernel.Persistence.Npgsql.Errors;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.Npgsql.Tests.Errors;

/// <summary>
/// A real <c>statement_timeout</c> expiry (SQLSTATE 57014) classifies as <see cref="ErrorType.Timeout"/>; a lock
/// that is not available (55P03) stays <see cref="ErrorType.Conflict"/>; a token cancellation stays a cancellation.
/// </summary>
public sealed class StatementTimeoutTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture = new();
    private NpgsqlDataSource? _dataSource;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();

        await _fixture.DisposeAsync();
    }

    private async Task SleepUnderStatementTimeoutAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource!.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SET LOCAL statement_timeout = 200; SELECT pg_sleep(10);", connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    [Fact]
    public async Task ExpiredStatementTimeout_IsATimeout_ThroughPostgresErrorMapping()
    {
        var result = await PostgresErrorMapping.TryAsync(() => SleepUnderStatementTimeoutAsync());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Timeout);
        result.Error.Code.Should().Be(PostgresClassifiedErrorCodes.StatementTimeout);
        result.Error.Code.Should().Be("persistence.postgresql.statement_timeout");
    }

    [Fact]
    public async Task ExpiredStatementTimeout_ClassifiesAsTransientTimeout()
    {
        var exception = await FluentActions.Awaiting(() => SleepUnderStatementTimeoutAsync())
            .Should().ThrowAsync<PostgresException>();

        var classification = PostgresExceptionClassifier.Classify(exception.Which);

        classification.Should().NotBeNull();
        classification!.Error.Type.Should().Be(ErrorType.Timeout);
        classification.IsTransient.Should().BeTrue();
    }

    [Fact]
    public async Task LockNotAvailable_StaysAConflict_NegativeControl()
    {
        await using var holder = await _dataSource!.OpenConnectionAsync();
        await using (var create = new NpgsqlCommand("CREATE TABLE IF NOT EXISTS locked_row (id int primary key); INSERT INTO locked_row VALUES (1) ON CONFLICT DO NOTHING;", holder))
            await create.ExecuteNonQueryAsync();

        await using var holderTransaction = await holder.BeginTransactionAsync();
        await using (var lockRow = new NpgsqlCommand("SELECT id FROM locked_row WHERE id = 1 FOR UPDATE", holder, holderTransaction))
            await lockRow.ExecuteScalarAsync();

        var result = await PostgresErrorMapping.TryAsync(async () =>
        {
            await using var connection = await _dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand("SELECT id FROM locked_row WHERE id = 1 FOR UPDATE NOWAIT", connection);
            await command.ExecuteScalarAsync();
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(PostgresClassifiedErrorCodes.LockTimeout);
    }

    [Fact]
    public async Task CancellationByToken_StaysAnOperationCanceledException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var act = () => PostgresErrorMapping.TryAsync(async () =>
        {
            await using var connection = await _dataSource!.OpenConnectionAsync(cts.Token);
            await using var command = new NpgsqlCommand("SELECT pg_sleep(10)", connection);
            await command.ExecuteNonQueryAsync(cts.Token);
        });

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
