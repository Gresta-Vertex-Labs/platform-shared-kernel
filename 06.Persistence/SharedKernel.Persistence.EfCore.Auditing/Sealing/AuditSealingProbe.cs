using System.Data;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Auditing.Storage;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Auditing.Sealing;

/// <summary>
/// The default <see cref="IAuditSealingProbe"/>: counts committed records that have no link, scanning the
/// <c>(insert_xid, id)</c> index from the process's <see cref="AuditSealedFloor"/> with an anti-join on the link
/// table's primary key.
/// </summary>
/// <remarks>
/// Never trusts the link table for its bound (finding S5): a forged link cannot make the probe report no backlog. The
/// probe also advances the floor from what it saw below the transaction-id horizon, so repeated probes of a
/// process that does not seal stay cheap after the first.
/// </remarks>
internal sealed class AuditSealingProbe(IDbConnectionFactory connectionFactory, AuditSealedFloor sealedFloor, IClock clock) : IAuditSealingProbe
{
    private static readonly string FirstUnsealedSql =
        $"""
        SELECT r.insert_xid
        FROM {AuditLedgerSchema.RecordsTable} r
        WHERE r.insert_xid < @horizon AND {AuditSealingEngine.UnsealedPredicate}
        ORDER BY r.insert_xid, r.id
        LIMIT 1
        """;

    private static readonly string BacklogSql =
        $"""
        SELECT count(*), min(r.occurred_on)
        FROM {AuditLedgerSchema.RecordsTable} r
        WHERE {AuditSealingEngine.UnsealedPredicate}
        """;

    public async Task<AuditSealingHealth> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            // Learn the floor first: below a horizon read before the scan, the first record without a link bounds it.
            var horizon = await AuditSealingEngine.ReadHorizonAsync(connection, null, cancellationToken).ConfigureAwait(false);
            await using (var first = LedgerDb.CreateCommand(connection, null, FirstUnsealedSql))
            {
                LedgerDb.Add(first, "@horizon", horizon, DbType.Int64);
                LedgerDb.Add(first, "@floor", sealedFloor.Value, DbType.Int64);
                var firstUnsealed = await first.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                sealedFloor.Advance(firstUnsealed is long xid ? xid : horizon);
            }

            await using var command = LedgerDb.CreateCommand(connection, null, BacklogSql);
            LedgerDb.Add(command, "@floor", sealedFloor.Value, DbType.Int64);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            var count = reader.GetInt64(0);
            DateTimeOffset? oldest = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
            var now = clock.UtcNow;
            var lag = oldest is { } o && now > o ? now - o : TimeSpan.Zero;
            return new AuditSealingHealth(count, oldest, lag);
        }
    }
}
