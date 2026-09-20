using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// Fails loudly, before executing it, any generated <c>UPDATE</c> or <c>DELETE</c> command that
/// targets the audit-record table — including <c>ExecuteUpdateAsync</c>/<c>ExecuteDeleteAsync</c> and a
/// raw <c>ExecuteSqlRaw</c>/<c>FromSqlRaw</c> fragment, none of which ever populate
/// <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker"/> and so are entirely
/// invisible to <see cref="AuditRecordImmutabilityInterceptor"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>DbCommandInterceptor</c> is the ONE EF Core extension point that sees every generated
/// command regardless of which higher-level API produced it — the same mechanism
/// <c>SharedKernel.Persistence.EfCore.Encryption</c>'s <c>EncryptedColumnEqualityGuardInterceptor</c>
/// already uses for an analogous "inspect the generated SQL text before it runs" guard. Like that
/// sibling, this is a heuristic over the FINAL, provider-generated SQL text, not a real SQL parser — see
/// "What it does not catch" below.
/// </para>
/// <para>
/// <strong>What it catches:</strong> a heuristic, regex-based scan of the FINAL, provider-generated SQL
/// text for <c>UPDATE</c>, <c>DELETE [FROM]</c>, <c>TRUNCATE [TABLE]</c>, <c>DROP TABLE</c> or <c>ALTER
/// TABLE</c> naming the audit table as their target — including an optional <c>ONLY</c> keyword and an
/// optional schema qualification (<c>"schema"."audit_records"</c>), whether or not the identifier is
/// quoted. Checked against EVERY command-execution path this interceptor overrides (<c>NonQuery</c>,
/// <c>Reader</c>, and <c>Scalar</c> — not only <c>NonQuery</c>; an <c>UPDATE</c>/<c>DELETE</c> carrying a
/// <c>RETURNING</c> clause, e.g. against a concurrency-token-bearing entity, is executed through the
/// READER path, not the non-query one, and would otherwise sail straight through). This covers
/// <c>ExecuteUpdateAsync</c>/<c>ExecuteDeleteAsync</c> (EF Core 10's bulk-mutation surface, which never
/// populates the change tracker) and a raw <c>Database.ExecuteSqlRawAsync</c>/
/// <c>ExecuteSqlInterpolatedAsync</c> call reached through THIS <see cref="DbContext"/>.
/// </para>
/// <para>
/// <strong>What it does not catch:</strong> (1) a write issued from a DIFFERENT connection/process
/// entirely — a raw <c>psql</c> session, a different service sharing the database, a DBA tool; (2) a
/// single <c>TRUNCATE</c>/<c>DROP</c> statement naming SEVERAL tables in a comma-separated list where
/// the audit table is not the FIRST one named — this is a heuristic text scan, not a SQL parser, and
/// does not tokenize a multi-table list. Both classes of write are exactly what the PostgreSQL migration
/// helper's database-level trigger (<c>AuditImmutabilityMigrationBuilderExtensions.CreateImmutabilityTrigger</c>,
/// applied to the audit table) exists for — see its own remarks. Applying that trigger is MANDATORY for
/// a production deployment; this interceptor and <see cref="AuditRecordImmutabilityInterceptor"/> are
/// the application-level, early-fail layer of a defense-in-depth stack, never a substitute for the
/// database-level one.
/// </para>
/// </remarks>
public sealed class AuditRecordMutationGuardInterceptor : DbCommandInterceptor
{
    private static readonly Regex MutatingStatementPattern = new(
        $@"\b(?:UPDATE|DELETE\s+FROM|TRUNCATE(?:\s+TABLE)?|DROP\s+TABLE|ALTER\s+TABLE)\s+(?:ONLY\s+)?" +
        $@"(?:""?[A-Za-z_][\w$]*""?\s*\.\s*)?""?{Regex.Escape(AuditSchema.TableName)}""?(?!\w)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Check(command);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Check(command);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Check(command);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Check(command);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Check(command);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Check(command);
        return ValueTask.FromResult(result);
    }

    private static void Check(DbCommand command)
    {
        if (string.IsNullOrEmpty(command.CommandText))
            return;

        if (MutatingStatementPattern.IsMatch(command.CommandText))
        {
            throw new AuditRecordImmutableException(
                $"A generated statement mutates the '{AuditSchema.TableName}' table (UPDATE, DELETE, " +
                "TRUNCATE, DROP TABLE, or ALTER TABLE). The audit trail is append-only — these are " +
                "structurally forbidden, including bulk ExecuteUpdate/ExecuteDelete and raw SQL. " +
                "Record a new AuditRecord via IAuditTrailWriter instead.");
        }
    }
}
