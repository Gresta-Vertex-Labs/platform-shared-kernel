using System.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Auditing.Diagnostics;
using SharedKernel.Persistence.EfCore.Auditing.Storage;

namespace SharedKernel.Persistence.EfCore.Auditing.SelfCheck;

/// <summary>
/// Verifies that the database — not only this application — keeps the ledger append-only: the runtime role
/// is no superuser and no member of the tables' owner role, holds no UPDATE/DELETE/TRUNCATE privilege on
/// them (DELETE is expected on the payload table), and every append-only trigger exists with
/// <c>tgenabled = 'A'</c> (fires even under <c>session_replication_role = replica</c>).
/// </summary>
internal sealed class AuditLedgerSelfCheck(IDbConnectionFactory connectionFactory)
{
    public async Task<IReadOnlyList<string>> RunAsync(CancellationToken cancellationToken)
    {
        var findings = new List<string>();
        var connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using (var role = LedgerDb.CreateCommand(connection, null,
                "SELECT current_user::text, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user"))
            await using (var reader = await role.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && reader.GetBoolean(1))
                    findings.Add($"the runtime role '{reader.GetString(0)}' is a superuser and can disable the ledger's triggers.");
            }

            foreach (var table in AuditLedgerSchema.Tables)
                await CheckTableAsync(connection, table, findings, cancellationToken).ConfigureAwait(false);
        }

        return findings;
    }

    private static async Task CheckTableAsync(System.Data.Common.DbConnection connection, string table, List<string> findings, CancellationToken cancellationToken)
    {
        var allowDelete = table == AuditLedgerSchema.PayloadsTable;
        long oid;

        await using (var command = LedgerDb.CreateCommand(connection, null,
            """
            SELECT c.oid::bigint,
                   pg_has_role(c.relowner, 'MEMBER'),
                   has_table_privilege(c.oid, 'UPDATE'),
                   has_table_privilege(c.oid, 'DELETE'),
                   has_table_privilege(c.oid, 'TRUNCATE'),
                   c.relrowsecurity
            FROM pg_class c
            WHERE c.oid = to_regclass(@table)
            """))
        {
            LedgerDb.Add(command, "@table", table, DbType.String);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                findings.Add($"table '{table}' does not exist; apply migrationBuilder.CreateAuditLedgerTable().");
                return;
            }

            oid = reader.GetInt64(0);
            if (reader.GetBoolean(1))
                findings.Add($"the runtime role owns (or is a member of the owner of) '{table}' and can disable its triggers or drop it.");
            if (reader.GetBoolean(2))
                findings.Add($"the runtime role has UPDATE on '{table}'.");
            if (!allowDelete && reader.GetBoolean(3))
                findings.Add($"the runtime role has DELETE on '{table}'.");
            if (reader.GetBoolean(4))
                findings.Add($"the runtime role has TRUNCATE on '{table}'.");
            if (reader.GetBoolean(5))
                findings.Add($"row-level security is enabled on '{table}'; the ledger enforces tenant isolation itself and the sealer must see every tenant's rows, so ledger tables must not use RLS.");
        }

        var present = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var command = LedgerDb.CreateCommand(connection, null,
            "SELECT tgname::text, tgenabled::text FROM pg_trigger WHERE tgrelid = @oid::oid AND NOT tgisinternal"))
        {
            LedgerDb.Add(command, "@oid", oid, DbType.Int64);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                present[reader.GetString(0)] = reader.GetString(1);
        }

        foreach (var trigger in AuditLedgerSchema.ExpectedTriggers[table])
        {
            if (!present.TryGetValue(trigger, out var enabled))
                findings.Add($"trigger '{trigger}' on '{table}' is missing.");
            else if (enabled != "A")
                findings.Add($"trigger '{trigger}' on '{table}' is not ENABLE ALWAYS (tgenabled = '{enabled}').");
        }
    }
}

/// <summary>Runs <see cref="AuditLedgerSelfCheck"/> at startup according to <see cref="AuditLedgerOptions.SelfCheck"/>.</summary>
internal sealed class AuditLedgerSelfCheckHostedService(
    AuditLedgerSelfCheck check,
    IOptions<AuditLedgerOptions> options,
    ILogger<AuditLedgerSelfCheckHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var mode = options.Value.SelfCheck;
        if (mode == AuditSelfCheckMode.Off)
            return;

        var findings = await check.RunAsync(cancellationToken).ConfigureAwait(false);
        if (findings.Count == 0)
        {
            AuditingLog.SelfCheckPassed(logger);
            return;
        }

        foreach (var finding in findings)
            AuditingLog.SelfCheckFinding(logger, finding);

        if (mode == AuditSelfCheckMode.Fail)
        {
            AuditingLog.SelfCheckFailed(logger, findings.Count);
            throw new InvalidOperationException(
                "The audit ledger self-check failed: " + string.Join(" ", findings) +
                " See the SharedKernel.Persistence.EfCore.Auditing README for the role and grant script.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
