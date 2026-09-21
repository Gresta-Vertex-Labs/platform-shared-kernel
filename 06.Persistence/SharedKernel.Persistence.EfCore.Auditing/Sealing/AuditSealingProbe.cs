using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Auditing.Storage;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Auditing.Sealing;

/// <summary>
/// The default <see cref="IAuditSealingProbe"/>: counts committed records after the sealer's watermark,
/// a range scan of the <c>(insert_xid, id)</c> index, so it stays cheap on a large ledger.
/// </summary>
internal sealed class AuditSealingProbe(IDbConnectionFactory connectionFactory, IClock clock) : IAuditSealingProbe
{
    private const string ProbeSql =
        $"""
        SELECT count(*), min(r.occurred_on)
        FROM {AuditLedgerSchema.RecordsTable} r
        LEFT JOIN LATERAL (
            SELECT l.record_insert_xid AS x, l.record_id AS id
            FROM {AuditLedgerSchema.LinksTable} l
            ORDER BY l.record_insert_xid DESC, l.record_id DESC
            LIMIT 1) w ON TRUE
        WHERE w.x IS NULL OR (r.insert_xid, r.id) > (w.x, w.id)
        """;

    public async Task<AuditSealingHealth> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = LedgerDb.CreateCommand(connection, null, ProbeSql);
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
