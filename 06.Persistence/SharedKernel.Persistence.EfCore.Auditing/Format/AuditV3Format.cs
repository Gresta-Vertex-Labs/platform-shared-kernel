using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SharedKernel.Execution.Auditing;
using SharedKernel.Execution.Context;

namespace SharedKernel.Persistence.EfCore.Auditing.Format;

/// <summary>The stored fields of one <c>audit_records</c> row — exactly what the AUDITv3 link MAC covers besides the chain fields.</summary>
internal sealed record LedgerRecordFields
{
    public required Guid Id { get; init; }
    public Guid? TenantId { get; init; }
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public required string Action { get; init; }
    public required AuditOutcome Outcome { get; init; }
    public string? ErrorCode { get; init; }
    public required string ActorId { get; init; }
    public required ActorKind ActorKind { get; init; }
    public string? ClientId { get; init; }
    public string? SessionId { get; init; }
    public string? ImpersonatorId { get; init; }
    public required string SourceService { get; init; }
    public string? CorrelationId { get; init; }
    public string? TraceId { get; init; }
    public string? ApprovalId { get; init; }
    public string? IdempotencyKey { get; init; }
    public required DateTimeOffset OccurredOn { get; init; }
    public required byte[] PayloadHash { get; init; }
    public int FormatVersion { get; init; } = AuditV3Format.Version;
}

/// <summary>
/// The AUDITv3 byte encodings — the single implementation of <c>AUDIT-FORMAT.md</c>. Every integer is
/// big-endian, every GUID is written in RFC 9562 (big-endian) byte order, every string is UTF-8 with a
/// 4-byte length prefix, every optional value is preceded by a presence byte (0 absent, 1 present), and
/// timestamps are whole microseconds since the Unix epoch (UTC).
/// </summary>
internal static class AuditV3Format
{
    /// <summary>The format version stored on records, links and checkpoints.</summary>
    public const int Version = 3;

    /// <summary>The MAC algorithm of the default keyring.</summary>
    public const string HmacSha256 = "HMAC-SHA256";

    /// <summary>The size of a payload salt and of a payload commitment, in bytes.</summary>
    public const int SaltLength = 32;

    private static readonly byte[] LinkDomain = Encoding.ASCII.GetBytes("AUDITv3");
    private static readonly byte[] PayloadDomain = Encoding.ASCII.GetBytes("AUDITv3-PAYLOAD");
    private static readonly byte[] CheckpointDomain = Encoding.ASCII.GetBytes("AUDITv3-CHECKPOINT");

    /// <summary>Encodes the message a link MAC is computed over.</summary>
    public static byte[] EncodeLinkMessage(
        LedgerRecordFields record,
        long sequence,
        ReadOnlySpan<byte> previousMac,
        bool hasPreviousMac,
        string keyId,
        string algorithm)
    {
        var w = new CanonicalWriter(512);
        w.WriteBytes(LinkDomain);
        w.WriteInt32(record.FormatVersion);
        w.WriteString(algorithm);
        w.WriteString(keyId);
        w.WriteOptionalGuid(record.TenantId);
        w.WriteString(record.ResourceType);
        w.WriteInt64(sequence);
        w.WritePresence(hasPreviousMac);
        if (hasPreviousMac)
            w.WriteBytes(previousMac);
        w.WriteGuid(record.Id);
        w.WriteInt64(AuditTimestamp.ToUnixMicroseconds(record.OccurredOn));
        w.WriteString(record.ResourceId);
        w.WriteString(record.Action);
        w.WriteInt32((int)record.Outcome);
        w.WriteOptionalString(record.ErrorCode);
        w.WriteString(record.ActorId);
        w.WriteInt32((int)record.ActorKind);
        w.WriteOptionalString(record.ClientId);
        w.WriteOptionalString(record.SessionId);
        w.WriteOptionalString(record.ImpersonatorId);
        w.WriteString(record.SourceService);
        w.WriteOptionalString(record.CorrelationId);
        w.WriteOptionalString(record.TraceId);
        w.WriteOptionalString(record.ApprovalId);
        w.WriteOptionalString(record.IdempotencyKey);
        w.WriteBytes(record.PayloadHash);
        return w.ToArray();
    }

    /// <summary>Encodes the erasable payload (snapshots) the commitment hashes.</summary>
    public static byte[] EncodePayload(string? beforeSnapshot, string? afterSnapshot)
    {
        var w = new CanonicalWriter(64 + (beforeSnapshot?.Length ?? 0) + (afterSnapshot?.Length ?? 0));
        w.WriteBytes(PayloadDomain);
        w.WriteOptionalString(beforeSnapshot);
        w.WriteOptionalString(afterSnapshot);
        return w.ToArray();
    }

    /// <summary>Computes <c>SHA-256(salt ‖ payload)</c>.</summary>
    public static byte[] ComputePayloadCommitment(ReadOnlySpan<byte> salt, string? beforeSnapshot, string? afterSnapshot)
    {
        var payload = EncodePayload(beforeSnapshot, afterSnapshot);
        var input = new byte[salt.Length + payload.Length];
        salt.CopyTo(input);
        payload.CopyTo(input.AsSpan(salt.Length));
        return SHA256.HashData(input);
    }

    /// <summary>Encodes the message a checkpoint signature is computed over.</summary>
    public static byte[] EncodeCheckpoint(
        Guid id,
        Guid? tenantId,
        string resourceType,
        long sequence,
        ReadOnlySpan<byte> headMac,
        DateTimeOffset createdOn,
        string signingKeyId)
    {
        var w = new CanonicalWriter(192);
        w.WriteBytes(CheckpointDomain);
        w.WriteInt32(Version);
        w.WriteGuid(id);
        w.WriteOptionalGuid(tenantId);
        w.WriteString(resourceType);
        w.WriteInt64(sequence);
        w.WriteBytes(headMac);
        w.WriteInt64(AuditTimestamp.ToUnixMicroseconds(createdOn));
        w.WriteString(signingKeyId);
        return w.ToArray();
    }

    /// <summary>Encodes an already materialized checkpoint's signed fields.</summary>
    public static byte[] EncodeCheckpoint(AuditChainCheckpoint checkpoint) => EncodeCheckpoint(
        checkpoint.Id,
        checkpoint.TenantId,
        checkpoint.ResourceType,
        checkpoint.Sequence,
        checkpoint.HeadMac,
        checkpoint.CreatedOn,
        checkpoint.SigningKeyId);
}

/// <summary>Length-prefixed, big-endian primitive writer shared by every AUDITv3 encoding.</summary>
internal sealed class CanonicalWriter(int initialCapacity)
{
    private readonly ArrayBufferWriter<byte> _buffer = new(Math.Max(initialCapacity, 16));

    public void WriteInt32(int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(_buffer.GetSpan(4), value);
        _buffer.Advance(4);
    }

    public void WriteInt64(long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(_buffer.GetSpan(8), value);
        _buffer.Advance(8);
    }

    public void WritePresence(bool present)
    {
        _buffer.GetSpan(1)[0] = present ? (byte)1 : (byte)0;
        _buffer.Advance(1);
    }

    public void WriteGuid(Guid value)
    {
        // RFC 9562 network byte order — Guid's default TryWriteBytes is the mixed-endian Windows layout.
        if (!value.TryWriteBytes(_buffer.GetSpan(16), bigEndian: true, out var written) || written != 16)
            throw new InvalidOperationException("Failed to encode a GUID.");
        _buffer.Advance(16);
    }

    public void WriteOptionalGuid(Guid? value)
    {
        WritePresence(value.HasValue);
        if (value is { } v)
            WriteGuid(v);
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        WriteInt32(value.Length);
        value.CopyTo(_buffer.GetSpan(value.Length));
        _buffer.Advance(value.Length);
    }

    public void WriteString(string value) => WriteBytes(Encoding.UTF8.GetBytes(value));

    public void WriteOptionalString(string? value)
    {
        WritePresence(value is not null);
        if (value is not null)
            WriteString(value);
    }

    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();
}

/// <summary>Microsecond truncation and Unix-microsecond conversion matching PostgreSQL <c>timestamptz</c>.</summary>
internal static class AuditTimestamp
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMicrosecond;

    /// <summary>Truncates <paramref name="value"/> to whole microseconds and converts it to UTC.</summary>
    public static DateTimeOffset Truncate(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TicksPerMicrosecond), TimeSpan.Zero);
    }

    /// <summary>Converts <paramref name="value"/> to whole microseconds since the Unix epoch (UTC), truncating.</summary>
    public static long ToUnixMicroseconds(DateTimeOffset value) =>
        (value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / TicksPerMicrosecond;
}
