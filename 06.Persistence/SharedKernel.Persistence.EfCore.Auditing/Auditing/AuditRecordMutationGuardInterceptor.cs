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
/// <strong>What it catches:</strong> any <c>UPDATE</c> or <c>DELETE</c> statement whose target table
/// name (quoted or not) is the audit table — this covers <c>ExecuteUpdateAsync</c>/<c>ExecuteDeleteAsync</c>
/// (EF Core 10's bulk-mutation surface, which never populates the change tracker) and a raw
/// <c>Database.ExecuteSqlRawAsync</c>/<c>ExecuteSqlInterpolatedAsync</c> call reached through THIS
/// <see cref="DbContext"/>.
/// </para>
/// <para>
/// <strong>What it does not catch:</strong> a write issued from a DIFFERENT connection/process
/// entirely — a raw <c>psql</c> session, a different service sharing the database, a DBA tool. That
/// class of write is exactly what the PostgreSQL migration helper's database-level trigger
/// (<c>AuditImmutabilityMigrationBuilderExtensions.CreateImmutabilityTrigger</c>, applied to the audit
/// table) exists for — see its own remarks. Applying that trigger is MANDATORY for a production
/// deployment; this interceptor and <see cref="AuditRecordImmutabilityInterceptor"/> are the
/// application-level layer of a defense-in-depth stack, never a substitute for the database-level one.
/// </para>
/// </remarks>
public sealed class AuditRecordMutationGuardInterceptor : DbCommandInterceptor
{
    private static readonly Regex TargetTablePattern = new(
        $@"\b(UPDATE|DELETE\s+FROM)\s+""?{Regex.Escape(AuditSchema.TableName)}""?",
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

    private static void Check(DbCommand command)
    {
        if (string.IsNullOrEmpty(command.CommandText))
            return;

        if (TargetTablePattern.IsMatch(command.CommandText))
        {
            throw new AuditRecordImmutableException(
                $"A generated UPDATE or DELETE statement targets the '{AuditSchema.TableName}' table. " +
                "The audit trail is append-only — update and delete are structurally forbidden, " +
                "including bulk ExecuteUpdate/ExecuteDelete and raw SQL. Record a new AuditRecord via " +
                "IAuditTrailWriter instead.");
        }
    }
}
