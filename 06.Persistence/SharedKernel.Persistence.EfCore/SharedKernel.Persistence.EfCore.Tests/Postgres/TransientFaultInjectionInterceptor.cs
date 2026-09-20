using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace SharedKernel.Persistence.EfCore.Tests.Postgres;

/// <summary>
/// Test-only <see cref="DbCommandInterceptor"/> that throws a fabricated, genuinely
/// Npgsql-retry-recognized transient <see cref="PostgresException"/> (SQLSTATE <c>40001</c>,
/// <c>serialization_failure</c> — one of Npgsql's own built-in transient error codes) for the first
/// <c>failuresBeforeSuccess</c> non-query command executions, then lets every subsequent one through
/// untouched.
/// </summary>
/// <remarks>
/// The deterministic way to prove
/// <c>EfTransactionalUnitOfWork.ExecuteInTransactionAsync</c>'s retry-safety against REAL PostgreSQL
/// without needing to orchestrate a genuine concurrent-transaction serialization conflict. Because
/// the interceptor throws BEFORE the command ever reaches the server, a "failed" attempt never
/// writes anything — so a subsequent SUCCESSFUL attempt is the only one that can ever leave a row
/// behind, which is exactly what proves "retry does not double-apply": <see cref="AttemptsObserved"/>
/// can be greater than 1 while the database still ends up with exactly one row.
/// </remarks>
internal sealed class TransientFaultInjectionInterceptor : DbCommandInterceptor
{
    private int _remainingFailures;

    public TransientFaultInjectionInterceptor(int failuresBeforeSuccess) => _remainingFailures = failuresBeforeSuccess;

    /// <summary>The number of command executions this interceptor has observed so far.</summary>
    public int AttemptsObserved { get; private set; }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        MaybeFail();
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// EF Core's Npgsql provider issues an <c>INSERT... RETURNING</c> (to read back the generated
    /// <c>xmin</c> concurrency token and any other database-generated values) via
    /// <c>ExecuteReaderAsync</c>, NOT <c>ExecuteNonQueryAsync</c> — confirmed empirically:
    /// <see cref="NonQueryExecutingAsync"/> alone never observed an attempt at all for
    /// <see cref="PgOrderAggregate"/>'s insert. Both overrides share the same counter/throw logic so
    /// callers never need to know which path a given command takes.
    /// </remarks>
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        MaybeFail();
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void MaybeFail()
    {
        AttemptsObserved++;

        if (_remainingFailures > 0)
        {
            _remainingFailures--;
            throw new PostgresException(
                "Simulated transient failure injected by TransientFaultInjectionInterceptor.",
                severity: "ERROR",
                invariantSeverity: "ERROR",
                sqlState: "40001"); // serialization_failure — a built-in Npgsql-recognized transient code.
        }
    }
}
