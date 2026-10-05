using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Auditing.Diagnostics;
using SharedKernel.Persistence.EfCore.Auditing.Format;
using SharedKernel.Persistence.EfCore.Auditing.Storage;

namespace SharedKernel.Persistence.EfCore.Auditing.Verification;

/// <summary>A known-good point to start a walk from: a signed checkpoint's sequence and head MAC.</summary>
internal readonly record struct ChainAnchor(long Sequence, byte[] Mac);

/// <summary>
/// Walks a chain in sequence order and proves each link: contiguous sequence, known key, no key
/// regression, a valid MAC over the record, a previous-MAC that matches the MAC actually found before it,
/// and a payload that still matches its commitment.
/// </summary>
internal sealed class AuditChainVerifier(
    IDbConnectionFactory connectionFactory,
    IAuditRecordAuthenticator authenticator,
    ILogger logger)
{
    private const int O = LedgerDb.RecordColumnCount;

    /// <summary>
    /// Verifies <paramref name="chain"/> from genesis (<paramref name="anchor"/> <see langword="null"/>) or
    /// from a checkpoint's anchor (the anchor record is re-authenticated, A15), optionally requiring the
    /// chain to still reach <paramref name="expectedHead"/>.
    /// </summary>
    public async Task<(AuditChainVerificationResult Result, byte[]? HeadMac)> VerifyCoreAsync(
        ChainId chain,
        ChainAnchor? anchor,
        ChainAnchor? expectedHead,
        bool requirePayloads,
        CancellationToken cancellationToken)
    {
        using var activity = AuditingMeter.ActivitySource.StartActivity("audit.verify");
        activity?.SetTag("audit.resource_type", chain.ResourceType);

        var connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = LedgerDb.CreateCommand(connection, null, string.Empty);
            var tenant = LedgerDb.TenantPredicate(command, "l.tenant_id", chain.TenantId);
            LedgerDb.Add(command, "@resource_type", chain.ResourceType, DbType.String);
            LedgerDb.Add(command, "@from", anchor?.Sequence ?? 1L, DbType.Int64);
            command.CommandText =
                $"""
                SELECT {LedgerDb.RecordColumns},
                       p.salt, p.before_snapshot, p.after_snapshot, (p.record_id IS NOT NULL) AS has_payload,
                       l.sequence, l.previous_mac, l.mac, l.key_id, l.algorithm, l.format_version
                FROM {AuditLedgerSchema.LinksTable} l
                JOIN {AuditLedgerSchema.RecordsTable} r ON r.id = l.record_id
                LEFT JOIN {AuditLedgerSchema.PayloadsTable} p ON p.record_id = r.id
                WHERE {tenant} AND l.resource_type = @resource_type AND l.sequence >= @from
                ORDER BY l.sequence
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            var walk = new Walk(anchor?.Sequence ?? 1L);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var failure = await CheckRowAsync(reader, chain, anchor, walk, requirePayloads, cancellationToken).ConfigureAwait(false);
                if (failure is not null)
                    return (Report(chain, failure), null);

                if (expectedHead is { } head && head.Sequence == walk.LastSequence)
                    walk.CapturedHeadMac = walk.PreviousMac;
            }

            if (anchor is { } a && walk.Checked == 0)
                return (Report(chain, walk.Fail(AuditVerificationStatus.Broken, AuditVerificationFailureKind.AnchorMismatch, a.Sequence, null, "the anchored record is no longer in the chain")), null);

            if (expectedHead is { } expected)
            {
                if (walk.LastSequence < expected.Sequence)
                {
                    return (Report(chain, walk.Fail(AuditVerificationStatus.Broken, AuditVerificationFailureKind.TailTruncated, expected.Sequence, null,
                        $"the chain ends at sequence {walk.LastSequence} but a signed checkpoint attests sequence {expected.Sequence}")), null);
                }

                if (walk.CapturedHeadMac is null || !walk.CapturedHeadMac.AsSpan().SequenceEqual(expected.Mac))
                {
                    return (Report(chain, walk.Fail(AuditVerificationStatus.Broken, AuditVerificationFailureKind.AnchorMismatch, expected.Sequence, null,
                        "the record at the expected head's sequence is not the one the checkpoint attests (the tail was replaced)")), null);
                }
            }

            var intact = new AuditChainVerificationResult
            {
                Status = AuditVerificationStatus.Intact,
                RecordsChecked = walk.Checked,
                ErasedPayloads = walk.Erased,
                HeadSequence = walk.Intact == 0 ? null : walk.LastSequence,
            };
            return (intact, walk.PreviousMac);
        }
    }

    /// <summary>Same as <see cref="VerifyCoreAsync"/>, without the head MAC.</summary>
    public async Task<AuditChainVerificationResult> VerifyAsync(
        ChainId chain,
        ChainAnchor? anchor,
        ChainAnchor? expectedHead,
        bool requirePayloads,
        CancellationToken cancellationToken) =>
        (await VerifyCoreAsync(chain, anchor, expectedHead, requirePayloads, cancellationToken).ConfigureAwait(false)).Result;

    /// <summary>Verifies a single sealed record against its own link (MAC and payload; not its neighbours).</summary>
    public async Task<AuditRecordVerificationResult> VerifyRecordAsync(Guid recordId, bool requirePayload, CancellationToken cancellationToken)
    {
        var connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = LedgerDb.CreateCommand(connection, null,
                $"""
                SELECT {LedgerDb.RecordColumns},
                       p.salt, p.before_snapshot, p.after_snapshot, (p.record_id IS NOT NULL) AS has_payload,
                       l.sequence, l.previous_mac, l.mac, l.key_id, l.algorithm, l.format_version
                FROM {AuditLedgerSchema.RecordsTable} r
                LEFT JOIN {AuditLedgerSchema.LinksTable} l ON l.record_id = r.id
                LEFT JOIN {AuditLedgerSchema.PayloadsTable} p ON p.record_id = r.id
                WHERE r.id = @id
                """);
            LedgerDb.Add(command, "@id", recordId, DbType.Guid);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new KeyNotFoundException($"Audit record '{recordId}' does not exist.");

            var hasPayload = reader.GetBoolean(O + 3);
            if (reader.IsDBNull(O + 4))
                return new() { Status = AuditVerificationStatus.Unverifiable, FailureKind = AuditVerificationFailureKind.NotSealed, Reason = "the record is not sealed yet", PayloadErased = !hasPayload };

            var row = ReadRow(reader);
            var key = await authenticator.FindKeyAsync(row.KeyId, cancellationToken).ConfigureAwait(false);
            if (key is null || !string.Equals(row.Algorithm, authenticator.Algorithm, StringComparison.Ordinal) || row.LinkFormatVersion != AuditV3Format.Version)
                return new() { Status = AuditVerificationStatus.Unverifiable, FailureKind = AuditVerificationFailureKind.UnknownKey, Reason = $"key '{row.KeyId}' ({row.Algorithm}) is not available", PayloadErased = !hasPayload };

            if (!await VerifyMacAsync(row, cancellationToken).ConfigureAwait(false))
                return new() { Status = AuditVerificationStatus.Broken, FailureKind = AuditVerificationFailureKind.HashMismatch, Reason = "the record's MAC does not match its content", PayloadErased = !hasPayload };

            if (row.HasPayload && !PayloadMatches(row))
                return new() { Status = AuditVerificationStatus.Broken, FailureKind = AuditVerificationFailureKind.HashMismatch, Reason = "the payload does not match its commitment" };

            if (!row.HasPayload && requirePayload)
                return new() { Status = AuditVerificationStatus.Unverifiable, FailureKind = AuditVerificationFailureKind.PayloadErased, Reason = "the payload was erased", PayloadErased = true };

            return new() { Status = AuditVerificationStatus.Intact, PayloadErased = !row.HasPayload };
        }
    }

    private async Task<Failure?> CheckRowAsync(
        DbDataReader reader,
        ChainId chain,
        ChainAnchor? anchor,
        Walk walk,
        bool requirePayloads,
        CancellationToken cancellationToken)
    {
        var row = ReadRow(reader);
        var isAnchor = anchor is not null && walk.Checked == 0;

        if (row.Sequence != walk.Expected)
        {
            return isAnchor
                ? walk.Fail(AuditVerificationStatus.Broken, AuditVerificationFailureKind.AnchorMismatch, walk.Expected, null, "the anchored record is no longer in the chain")
                : walk.Fail(AuditVerificationStatus.Broken, AuditVerificationFailureKind.SequenceGap, walk.Expected, row.Fields.Id,
                    $"expected sequence {walk.Expected} but found {row.Sequence} (a record or seal was deleted or renumbered)");
        }

        walk.Checked++;

        if (row.Fields.TenantId != chain.TenantId || !string.Equals(row.Fields.ResourceType, chain.ResourceType, StringComparison.Ordinal))
            return walk.Fail(AuditVerificationStatus.Broken, AuditVerificationFailureKind.HashMismatch, row.Sequence, row.Fields.Id, "the sealed record belongs to a different chain");

        var key = await authenticator.FindKeyAsync(row.KeyId, cancellationToken).ConfigureAwait(false);
        if (key is null || !string.Equals(row.Algorithm, authenticator.Algorithm, StringComparison.Ordinal) ||
            row.LinkFormatVersion != AuditV3Format.Version || row.Fields.FormatVersion != AuditV3Format.Version)
        {
            return walk.Fail(AuditVerificationStatus.Unverifiable, AuditVerificationFailureKind.UnknownKey, row.Sequence, row.Fields.Id,
                $"the record is sealed under key '{row.KeyId}' ({row.Algorithm}, format {row.LinkFormatVersion}), which the configured authenticator does not provide");
        }

        if (key.Value.Order < walk.MaxOrder)
        {
            return walk.Fail(AuditVerificationStatus.Broken, AuditVerificationFailureKind.KeyRegression, row.Sequence, row.Fields.Id,
                $"the record is sealed under key '{row.KeyId}' (order {key.Value.Order}) after a record sealed under a newer key (order {walk.MaxOrder})");
        }

        if (isAnchor && !row.Mac.AsSpan().SequenceEqual(anchor!.Value.Mac))
            return walk.Fail(AuditVerificationStatus.Broken, AuditVerificationFailureKind.AnchorMismatch, row.Sequence, row.Fields.Id, "the anchored record's MAC differs from the checkpoint");

        if (!await VerifyMacAsync(row, cancellationToken).ConfigureAwait(false))
            return walk.Fail(AuditVerificationStatus.Broken, AuditVerificationFailureKind.HashMismatch, row.Sequence, row.Fields.Id, "the record's MAC does not match its content (edited without the key)");

        if (!isAnchor)
        {
            var linked = row.PreviousMac is null ? walk.PreviousMac is null : walk.PreviousMac is not null && row.PreviousMac.AsSpan().SequenceEqual(walk.PreviousMac);
            if (!linked)
            {
                return walk.Fail(AuditVerificationStatus.Broken, AuditVerificationFailureKind.LinkMismatch, row.Sequence, row.Fields.Id,
                    "the record's previous-MAC does not match the MAC of the record before it (a record was re-sealed)");
            }
        }

        if (row.HasPayload)
        {
            if (!PayloadMatches(row))
                return walk.Fail(AuditVerificationStatus.Broken, AuditVerificationFailureKind.HashMismatch, row.Sequence, row.Fields.Id, "the payload does not match its commitment (snapshots altered)");
        }
        else
        {
            if (requirePayloads)
                return walk.Fail(AuditVerificationStatus.Unverifiable, AuditVerificationFailureKind.PayloadErased, row.Sequence, row.Fields.Id, "the payload was erased");
            walk.Erased++;
        }

        walk.MaxOrder = Math.Max(walk.MaxOrder, key.Value.Order);
        walk.PreviousMac = row.Mac;
        walk.LastSequence = row.Sequence;
        walk.Intact++;
        walk.Expected++;
        return null;
    }

    private async ValueTask<bool> VerifyMacAsync(Row row, CancellationToken cancellationToken)
    {
        var message = AuditV3Format.EncodeLinkMessage(
            row.Fields, row.Sequence, row.PreviousMac, row.PreviousMac is not null, row.KeyId, row.Algorithm);
        return await authenticator.VerifyMacAsync(row.KeyId, message, row.Mac, cancellationToken).ConfigureAwait(false);
    }

    private static bool PayloadMatches(Row row) =>
        row.Salt is not null &&
        AuditV3Format.ComputePayloadCommitment(row.Salt, row.BeforeSnapshot, row.AfterSnapshot).AsSpan().SequenceEqual(row.Fields.PayloadHash);

    private static Row ReadRow(DbDataReader reader) => new(
        LedgerDb.ReadFields(reader),
        LedgerDb.GetNullableBytes(reader, O),
        LedgerDb.GetNullableString(reader, O + 1),
        LedgerDb.GetNullableString(reader, O + 2),
        reader.GetBoolean(O + 3),
        reader.GetInt64(O + 4),
        LedgerDb.GetNullableBytes(reader, O + 5),
        reader.GetFieldValue<byte[]>(O + 6),
        reader.GetString(O + 7),
        reader.GetString(O + 8),
        reader.GetInt32(O + 9));

    private AuditChainVerificationResult Report(ChainId chain, Failure failure)
    {
        AuditingLog.ChainVerificationFailed(logger, chain.TenantLabel, chain.ResourceType, failure.Result.Status, failure.Result.FailureKind, failure.Result.FailedAtSequence, failure.Result.Reason);
        AuditingMeter.RecordVerificationFailure(failure.Result.FailureKind);
        Activity.Current?.SetTag(AuditingMeter.FailureKindTag, failure.Result.FailureKind.ToString());
        return failure.Result;
    }

    private sealed record Row(
        LedgerRecordFields Fields,
        byte[]? Salt,
        string? BeforeSnapshot,
        string? AfterSnapshot,
        bool HasPayload,
        long Sequence,
        byte[]? PreviousMac,
        byte[] Mac,
        string KeyId,
        string Algorithm,
        int LinkFormatVersion);

    private sealed record Failure(AuditChainVerificationResult Result);

    private sealed class Walk(long start)
    {
        public long Expected { get; set; } = start;
        public long LastSequence { get; set; } = start - 1;
        public long Checked { get; set; }
        public long Intact { get; set; }
        public long Erased { get; set; }
        public int MaxOrder { get; set; } = int.MinValue;
        public byte[]? PreviousMac { get; set; }
        public byte[]? CapturedHeadMac { get; set; }

        public Failure Fail(AuditVerificationStatus status, AuditVerificationFailureKind kind, long sequence, Guid? recordId, string reason) =>
            new(new AuditChainVerificationResult
            {
                Status = status,
                FailureKind = kind,
                FailedAtSequence = sequence,
                FailedAtRecordId = recordId,
                Reason = reason,
                RecordsChecked = Checked,
                ErasedPayloads = Erased,
                HeadSequence = Intact == 0 ? null : LastSequence,
            });
    }
}
