using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Auditing.Storage;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Health;

namespace SharedKernel.Persistence.EfCore.Auditing.Sealing;

/// <summary>
/// The audit ledger's readiness probe (<see cref="AuditSealingReadiness.ProbeName"/>): counts committed records that
/// have no link, scanning the <c>(insert_xid, id)</c> index from the process's <see cref="AuditSealedFloor"/> with an
/// anti-join on the link table's primary key.
/// </summary>
/// <remarks>
/// Never trusts the link table for its bound (finding S5): a forged link cannot make the probe report no backlog. The
/// probe also advances the floor from what it saw below the transaction-id horizon, so repeated probes of a
/// process that does not seal stay cheap after the first.
/// </remarks>
internal sealed class AuditSealingProbe(
    IDbConnectionFactory connectionFactory,
    AuditSealedFloor sealedFloor,
    IClock clock,
    IOptions<AuditLedgerOptions> options) : IReadinessProbe
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

    public string Name => AuditSealingReadiness.ProbeName;

    public async Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        (long Count, DateTimeOffset? Oldest, TimeSpan Lag) tail;
        try
        {
            tail = await MeasureAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbException exception)
        {
            return ReadinessReport.Unhealthy(
                $"The audit ledger could not be read ({exception.GetType().Name}).",
                latency: Stopwatch.GetElapsedTime(started));
        }

        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [AuditSealingReadiness.UnsealedRecordsKey] = tail.Count,
            [AuditSealingReadiness.LagKey] = tail.Lag,
        };
        if (tail.Oldest is { } oldest)
            data[AuditSealingReadiness.OldestUnsealedOccurredOnKey] = oldest;

        var latency = Stopwatch.GetElapsedTime(started);
        var maxLag = (options.Value.Sealer ?? new AuditSealerOptions()).MaxReadyLag;
        return tail.Lag > maxLag
            ? ReadinessReport.Degraded($"The oldest unsealed audit record is {tail.Lag} old (allowed {maxLag}).", data, latency)
            : ReadinessReport.Healthy("Audit ledger sealing is keeping up.", data, latency);
    }

    private async Task<(long Count, DateTimeOffset? Oldest, TimeSpan Lag)> MeasureAsync(CancellationToken cancellationToken)
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
            return (count, oldest, lag);
        }
    }
}
