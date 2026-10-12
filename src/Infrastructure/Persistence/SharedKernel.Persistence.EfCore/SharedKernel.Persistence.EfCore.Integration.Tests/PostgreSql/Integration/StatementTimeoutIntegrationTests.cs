using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.PostgreSql.Integration;

/// <summary>
/// A real <c>statement_timeout</c> expiry (SQLSTATE 57014) during <c>SaveChanges</c>: a <see cref="DomainException"/>
/// whose error is <see cref="ErrorType.Timeout"/> when retry is off, and retried (never wrapped) when retry is on. The
/// timeout is produced by a table lock another connection holds; a missing row still answers the concurrency conflict.
/// </summary>
[Collection("PostgreSQL")]
public sealed class StatementTimeoutIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private string ConnectionString(string database, int statementTimeoutMilliseconds) =>
        new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = database,
            Options = $"-c statement_timeout={statementTimeoutMilliseconds}",
        }.ConnectionString;

    private static ConcurrencyTestDbContext CreateContext(string connectionString, int maxRetryCount, params Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ConcurrencyTestDbContext>();
        builder.UsePostgres(TestNpgsqlDataSources.Get(connectionString), o =>
        {
            o.MaxRetryCount = maxRetryCount;
            o.MaxRetryDelay = TimeSpan.FromMilliseconds(100);
        });
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);

        return new ConcurrencyTestDbContext(builder.Options, PersistenceContextDependencies.Create(new FakeAuditActorContext(), new FakeClock()));
    }

    private async Task<(NpgsqlConnection Holder, NpgsqlTransaction Transaction)> LockTableAsync(string database, ConcurrencyTestDbContext context)
    {
        var table = context.Model.FindEntityType(typeof(ConcurrentPgAggregate))!.GetTableName();
        var holder = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = database }.ConnectionString);
        await holder.OpenAsync();
        var transaction = await holder.BeginTransactionAsync();
        await using var command = new NpgsqlCommand($"LOCK TABLE \"{table}\" IN ACCESS EXCLUSIVE MODE", holder, transaction);
        await command.ExecuteNonQueryAsync();
        return (holder, transaction);
    }

    [Fact]
    public async Task SaveChanges_StatementTimeoutExpires_RetryOff_ThrowsADomainExceptionWithATimeoutError()
    {
        const string database = "sk_persistence_stmt_timeout_off";
        await using (var setup = CreateContext(ConnectionString(database, 60_000), maxRetryCount: 0))
            await setup.Database.EnsureCreatedAsync();

        await using var ctx = CreateContext(ConnectionString(database, 300), maxRetryCount: 0);
        var (holder, transaction) = await LockTableAsync(database, ctx);
        await using (holder)
        await using (transaction)
        {
            ctx.Aggregates.Add(new ConcurrentPgAggregate(ConcurrentPgId.New(), "blocked", new SystemClock()));

            var act = () => ctx.SaveChangesAsync();

            var thrown = (await act.Should().ThrowAsync<DomainException>()).Which;
            thrown.Error.Type.Should().Be(ErrorType.Timeout);
            thrown.Error.Code.Should().Be("persistence.postgresql.statement_timeout");
            thrown.InnerException.Should().BeOfType<DbUpdateException>();
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task SaveChanges_StatementTimeoutExpires_RetryOn_IsNotRetriedByNpgsqlsStrategy_AndStillAnswersATimeout()
    {
        // Npgsql's retrying strategy does not treat 57014 as transient (only 40001, 40P01, 55P03, 53xxx, 57P01-03, ...),
        // so a statement that exceeded statement_timeout is not blindly replayed: it surfaces once, classified.
        const string database = "sk_persistence_stmt_timeout_on";
        await using (var setup = CreateContext(ConnectionString(database, 60_000), maxRetryCount: 0))
            await setup.Database.EnsureCreatedAsync();

        var attempts = new TransientFaultInjectionInterceptor(failuresBeforeSuccess: 0);
        await using var ctx = CreateContext(ConnectionString(database, 300), maxRetryCount: 5, attempts);
        var (holder, transaction) = await LockTableAsync(database, ctx);
        await using (holder)
        await using (transaction)
        {
            ctx.Aggregates.Add(new ConcurrentPgAggregate(ConcurrentPgId.New(), "blocked", new SystemClock()));

            var act = () => ctx.SaveChangesAsync();

            var thrown = (await act.Should().ThrowAsync<DomainException>()).Which;
            thrown.Error.Type.Should().Be(ErrorType.Timeout);
            thrown.Error.Code.Should().Be("persistence.postgresql.statement_timeout");
            attempts.AttemptCount.Should().Be(1, "the strategy did not replay the timed-out statement");
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task SaveChanges_OfARowThatDoesNotExist_AnswersTheConcurrencyConflict_TheFakeRepositoryMirrors()
    {
        const string database = "sk_persistence_stmt_timeout_missing";
        await using var ctx = CreateContext(ConnectionString(database, 60_000), maxRetryCount: 0);
        await ctx.Database.EnsureCreatedAsync();

        ctx.Aggregates.Update(new ConcurrentPgAggregate(ConcurrentPgId.New(), "ghost", new SystemClock()));

        var thrown = (await FluentActions.Awaiting(() => ctx.SaveChangesAsync()).Should().ThrowAsync<ConflictException>()).Which;
        thrown.Error.Code.Should().Be("persistence.concurrency_conflict");
        thrown.Error.Type.Should().Be(ErrorType.Conflict);
        thrown.Error.Message.Should().Be("'ConcurrentPgAggregate' was changed or deleted by someone else. Reload it and retry.");
    }
}
