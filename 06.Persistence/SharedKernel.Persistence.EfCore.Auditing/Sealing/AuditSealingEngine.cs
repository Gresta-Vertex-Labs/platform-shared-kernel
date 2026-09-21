using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Auditing.Diagnostics;
using SharedKernel.Persistence.EfCore.Auditing.Format;
using SharedKernel.Persistence.EfCore.Auditing.Storage;
using SharedKernel.Persistence.Npgsql.Coordination;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Auditing.Sealing;

/// <summary>
/// One sealing pass: under the sealer advisory lock, take every committed-and-final unsealed record in
/// <c>(insert_xid, id)</c> order and append a link (sequence, previous MAC, MAC) for it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Commit-order safety.</strong> A record is sealable only when the transaction that inserted it is
/// older than <c>pg_snapshot_xmin(pg_current_snapshot())</c> — the oldest transaction still running. Every
/// transaction below that horizon has finished, so the set of records with <c>insert_xid</c> below it can no
/// longer grow: a long-running transaction that commits late holds the horizon back instead of having its
/// record skipped or sealed out of order. Records are sealed in <c>(insert_xid, id)</c> order.
/// </para>
/// <para>
/// <strong>No trusted watermark.</strong> A pass selects the records below the horizon that have no link
/// (<c>NOT EXISTS</c>, an anti-join on the link table's primary key), scanning the <c>(insert_xid, id)</c> index from
/// <see cref="AuditSealedFloor"/> — a bound this process learned itself, never the links' highest
/// <c>record_insert_xid</c>. A link forged with a large <c>record_insert_xid</c> therefore cannot stop sealing or hide
/// the backlog (finding S5). A link forged for a specific record makes that record look sealed; chain verification
/// reports it (its MAC does not verify), and running the sealer under its own role (<see cref="AuditSealerOptions.DataSourceName"/>)
/// with <c>INSERT</c> on the link and checkpoint tables revoked from the application role prevents it.
/// </para>
/// <para>
/// <strong>Single sealer.</strong> Each pass runs in one transaction that first takes
/// <c>pg_try_advisory_xact_lock</c> on <see cref="SealerLockKey"/>; an instance that does not get it skips
/// the pass. The unique <c>(tenant_id, resource_type, sequence)</c> index would reject a concurrent fork
/// even if the lock were bypassed. Transaction-scoped, so it also works behind a transaction-mode pooler.
/// </para>
/// </remarks>
internal sealed class AuditSealingEngine(
    AuditSealerConnectionFactory connectionFactory,
    AuditSealedFloor sealedFloor,
    IAuditRecordAuthenticator authenticator,
    IClock clock,
    IOptions<AuditLedgerOptions> options,
    ILogger<AuditSealingEngine> logger)
{
    /// <summary>The namespaced advisory-lock name (<c>sk:audit:sealer</c>), hashed to <see cref="SealerLockKey"/>.</summary>
    public static readonly string SealerLockName = AdvisoryLockKeys.Audit("sealer");

    /// <summary>The advisory-lock key, derived by the platform-wide <see cref="AdvisoryLockKeys.ToKey"/>.</summary>
    public static readonly long SealerLockKey = AdvisoryLockKeys.ToKey(SealerLockName);

    private const int LinkParameterCount = 10;

    /// <summary>Runs one pass. Returns whether the lock was acquired and how many records were sealed.</summary>
    public async Task<AuditSealPassResult> SealOnceAsync(CancellationToken cancellationToken)
    {
        using var activity = AuditingMeter.ActivitySource.StartActivity("audit.seal");
        var startedAt = Stopwatch.GetTimestamp();
        var batchSize = options.Value.Sealer.BatchSize;

        var connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                if (!await TryLockAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    AuditingLog.SealerLockHeldElsewhere(logger);
                    return new AuditSealPassResult(false, 0);
                }

                var horizon = await ReadHorizonAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                var batch = await ReadSealableAsync(connection, transaction, sealedFloor.Value, batchSize, cancellationToken).ConfigureAwait(false);
                if (batch.Count == 0)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    // Every record below the horizon read before the scan has a link.
                    sealedFloor.Advance(horizon);
                    return new AuditSealPassResult(true, 0);
                }

                var key = await authenticator.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false);
                var sealedOn = AuditTimestamp.Truncate(clock.UtcNow);
                var heads = new Dictionary<ChainId, (long Sequence, byte[]? Mac)>();
                var links = new List<Link>(batch.Count);

                foreach (var (record, insertXid) in batch)
                {
                    var chain = new ChainId(record.TenantId, record.ResourceType);
                    if (!heads.TryGetValue(chain, out var head))
                        head = await ReadHeadAsync(connection, transaction, chain, cancellationToken).ConfigureAwait(false);

                    var sequence = head.Sequence + 1;
                    var message = AuditV3Format.EncodeLinkMessage(record, sequence, head.Mac, head.Mac is not null, key.Id, authenticator.Algorithm);
                    var mac = await authenticator.ComputeMacAsync(key.Id, message, cancellationToken).ConfigureAwait(false);

                    links.Add(new Link(record.Id, chain, sequence, head.Mac, mac, key.Id, insertXid));
                    heads[chain] = (sequence, mac);
                }

                await InsertLinksAsync(connection, transaction, links, sealedOn, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                // The batch was the first unsealed records in (insert_xid, id) order, so every record of an earlier
                // transaction is now sealed; when the batch was not full, every record below the horizon is.
                sealedFloor.Advance(batch.Count < batchSize ? horizon : batch[^1].InsertXid);

                var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                var oldestAge = (clock.UtcNow - batch.Min(b => b.Record.OccurredOn)).TotalSeconds;
                AuditingMeter.RecordSealPass(links.Count, elapsed, oldestAge);
                AuditingLog.SealPassCompleted(logger, links.Count, elapsed);
                activity?.SetTag("audit.sealed_records", links.Count);
                return new AuditSealPassResult(true, links.Count);
            }
        }
    }

    /// <summary>Runs passes until one seals fewer than a full batch. Stops early when another instance holds the lock.</summary>
    public async Task<AuditSealPassResult> SealUntilDrainedAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        var batchSize = options.Value.Sealer.BatchSize;
        while (true)
        {
            var pass = await SealOnceAsync(cancellationToken).ConfigureAwait(false);
            if (!pass.LockAcquired)
                return new AuditSealPassResult(total > 0, total);

            total += pass.RecordsSealed;
            if (pass.RecordsSealed < batchSize)
                return new AuditSealPassResult(true, total);
        }
    }

    internal static async Task<bool> TryLockAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = LedgerDb.CreateCommand(connection, transaction, "SELECT pg_try_advisory_xact_lock(@key)");
        LedgerDb.Add(command, "@key", SealerLockKey, DbType.Int64);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    /// <summary>The oldest transaction id still running, as <c>bigint</c>: every transaction below it has ended.</summary>
    internal const string HorizonSql = "SELECT pg_snapshot_xmin(pg_current_snapshot())::text::bigint";

    /// <summary>The records without a link from <c>@floor</c> on: an index range scan with an anti-join.</summary>
    internal static readonly string UnsealedPredicate =
        $"r.insert_xid >= @floor AND NOT EXISTS (SELECT 1 FROM {AuditLedgerSchema.LinksTable} l WHERE l.record_id = r.id)";

    /// <summary>Reads the transaction-id horizon (<see cref="HorizonSql"/>).</summary>
    internal static async Task<long> ReadHorizonAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = LedgerDb.CreateCommand(connection, transaction, HorizonSql);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<List<(LedgerRecordFields Record, long InsertXid)>> ReadSealableAsync(
        DbConnection connection,
        DbTransaction transaction,
        long floor,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var command = LedgerDb.CreateCommand(connection, transaction, string.Empty);
        LedgerDb.Add(command, "@floor", floor, DbType.Int64);
        LedgerDb.Add(command, "@batch", batchSize, DbType.Int32);

        // The horizon is evaluated in the same statement (same snapshot) as the row selection. The first records
        // without a link in (insert_xid, id) order — never after a watermark read from the link table.
        command.CommandText =
            $"""
            SELECT {LedgerDb.RecordColumns}
            FROM {AuditLedgerSchema.RecordsTable} r
            WHERE r.insert_xid < (pg_snapshot_xmin(pg_current_snapshot())::text::bigint)
              AND {UnsealedPredicate}
            ORDER BY r.insert_xid, r.id
            LIMIT @batch
            """;

        var rows = new List<(LedgerRecordFields, long)>();
        await using var rowsReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rowsReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            rows.Add((LedgerDb.ReadFields(rowsReader), rowsReader.GetInt64(LedgerDb.RecordColumnCount - 1)));
        return rows;
    }

    internal static async Task<(long Sequence, byte[]? Mac)> ReadHeadAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ChainId chain,
        CancellationToken cancellationToken)
    {
        await using var command = LedgerDb.CreateCommand(connection, transaction, string.Empty);
        var tenant = LedgerDb.TenantPredicate(command, "tenant_id", chain.TenantId);
        LedgerDb.Add(command, "@resource_type", chain.ResourceType, DbType.String);
        command.CommandText =
            $"SELECT sequence, mac FROM {AuditLedgerSchema.LinksTable} WHERE {tenant} AND resource_type = @resource_type ORDER BY sequence DESC LIMIT 1";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? (reader.GetInt64(0), reader.GetFieldValue<byte[]>(1))
            : (0L, null);
    }

    private async Task InsertLinksAsync(
        DbConnection connection,
        DbTransaction transaction,
        List<Link> links,
        DateTimeOffset sealedOn,
        CancellationToken cancellationToken)
    {
        // Postgres allows 65535 bind parameters per statement.
        const int rowsPerStatement = 60000 / LinkParameterCount;
        for (var offset = 0; offset < links.Count; offset += rowsPerStatement)
        {
            var slice = links.Skip(offset).Take(rowsPerStatement).ToList();
            await using var command = LedgerDb.CreateCommand(connection, transaction, string.Empty);
            var values = new StringBuilder();
            LedgerDb.Add(command, "@sealed_on", sealedOn, DbType.DateTimeOffset);
            LedgerDb.Add(command, "@algorithm", authenticator.Algorithm, DbType.String);
            LedgerDb.Add(command, "@format_version", AuditV3Format.Version, DbType.Int32);

            for (var i = 0; i < slice.Count; i++)
            {
                var link = slice[i];
                LedgerDb.Add(command, $"@r{i}", link.RecordId, DbType.Guid);
                LedgerDb.Add(command, $"@t{i}", link.Chain.TenantId, DbType.Guid);
                LedgerDb.Add(command, $"@rt{i}", link.Chain.ResourceType, DbType.String);
                LedgerDb.Add(command, $"@s{i}", link.Sequence, DbType.Int64);
                LedgerDb.Add(command, $"@p{i}", link.PreviousMac, DbType.Binary);
                LedgerDb.Add(command, $"@m{i}", link.Mac, DbType.Binary);
                LedgerDb.Add(command, $"@k{i}", link.KeyId, DbType.String);
                LedgerDb.Add(command, $"@x{i}", link.InsertXid, DbType.Int64);
                if (i > 0)
                    values.Append(',');
                values.Append($"(@r{i}, @t{i}, @rt{i}, @s{i}, @p{i}, @m{i}, @k{i}, @algorithm, @format_version, @x{i}, @sealed_on)");
            }

            command.CommandText =
                $"INSERT INTO {AuditLedgerSchema.LinksTable} " +
                "(record_id, tenant_id, resource_type, sequence, previous_mac, mac, key_id, algorithm, format_version, record_insert_xid, sealed_on) " +
                $"VALUES {values}";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record Link(Guid RecordId, ChainId Chain, long Sequence, byte[]? PreviousMac, byte[] Mac, string KeyId, long InsertXid);
}
