using System.Buffers;
using System.Text;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.Abstractions.Context;

namespace SharedKernel.Persistence.EfCore.Auditing.Chain;

/// <summary>The keyed-hash algorithm name recorded on every <see cref="AuditRecord.HashAlgorithm"/>.</summary>
internal static class AuditHashAlgorithmNames
{
    public const string HmacSha256 = "HMAC-SHA256";
}

/// <summary>
/// Canonical-encoding, HMAC-keyed hashing helper shared by <c>EfAuditTrailWriter</c> (write-time) and
/// <c>EfAuditQueryService</c> (verify-time) — the ONE place the exact field order and encoding is
/// defined, so write-time and verify-time hashing can never drift apart.
/// </summary>
/// <remarks>
/// <para>
/// A length-prefixed BINARY encoding (never JSON, never delimiter-joined
/// text) with an explicit <c>"AUDITv1"</c> domain-separator prefix identifying both the scheme and its
/// version — a future schema change bumps <see cref="AuditRecord.SchemaVersion"/> and this encoding
/// together, never silently. Every optional field is encoded with an explicit one-byte presence flag
/// BEFORE its length-prefixed payload, so <see langword="null"/> and <see cref="string.Empty"/> produce
/// byte-for-byte DIFFERENT encodings — the old delimited-text encoding could not tell "empty string"
/// from "absent" apart.
/// </para>
/// <para>
/// The HMAC key itself is never part of the encoded/hashed bytes — it is the key
/// <see cref="SharedKernel.Cryptography.Signing.IHmacSigner.Sign"/> is called with (see
/// <see cref="ComputeHashHex"/>). Keyed with a secret held OUTSIDE this database
/// (<see cref="IAuditChainKeyProvider"/>), unlike an unkeyed content hash — an attacker with database
/// access alone cannot recompute a valid replacement digest for a tampered or forged record.
/// </para>
/// </remarks>
internal static class AuditRecordHasher
{
    private static readonly byte[] DomainSeparator = Encoding.ASCII.GetBytes("AUDITv1");

    /// <summary>The exact set of fields fed into the hash, independent of how the caller currently holds them.</summary>
    public readonly record struct Fields(
        Guid Id,
        Guid? TenantId,
        string ActorId,
        ActorKind ActorKind,
        string Action,
        string ResourceType,
        string ResourceId,
        long Sequence,
        long OccurredOnUtcMicroseconds,
        string? BeforeSnapshot,
        string? AfterSnapshot,
        string? CorrelationId,
        string? ApprovalId,
        AuditOutcome Outcome,
        string? ErrorCode,
        string? ClientId,
        string? SessionId,
        string? ImpersonatorId,
        string? SourceService,
        string? IdempotencyKey,
        string? PreviousRecordHash,
        int SchemaVersion);

    /// <summary>Builds the hashed field set from a fully-materialized <see cref="AuditRecord"/> (the verify-time path).</summary>
    public static Fields FromRecord(AuditRecord record) => new(
        record.Id,
        record.TenantId,
        record.ActorId,
        record.ActorKind,
        record.Action,
        record.ResourceType,
        record.ResourceId,
        record.Sequence,
        AuditTimestamp.ToUtcMicroseconds(record.OccurredOn),
        record.BeforeSnapshot,
        record.AfterSnapshot,
        record.CorrelationId,
        record.ApprovalId,
        record.Outcome,
        record.ErrorCode,
        record.ClientId,
        record.SessionId,
        record.ImpersonatorId,
        record.SourceService,
        record.IdempotencyKey,
        record.PreviousRecordHash,
        record.SchemaVersion);

    /// <summary>Computes the hex HMAC-SHA256 digest of the canonical encoding of <paramref name="fields"/>, keyed by <paramref name="key"/>.</summary>
    public static string ComputeHashHex(IHmacSigner signer, ReadOnlySpan<byte> key, in Fields fields)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        Encode(buffer, in fields);

        var mac = signer.Sign(buffer.WrittenSpan, key);
        return Convert.ToHexStringLower(mac);
    }

    private static void Encode(ArrayBufferWriter<byte> buffer, in Fields fields)
    {
        CanonicalEncoding.WriteBytes(buffer, DomainSeparator);
        CanonicalEncoding.WriteInt32(buffer, fields.SchemaVersion);
        CanonicalEncoding.WriteGuid(buffer, fields.Id);
        CanonicalEncoding.WriteOptionalGuid(buffer, fields.TenantId);
        CanonicalEncoding.WriteString(buffer, fields.ActorId);
        CanonicalEncoding.WriteInt32(buffer, (int)fields.ActorKind);
        CanonicalEncoding.WriteString(buffer, fields.Action);
        CanonicalEncoding.WriteString(buffer, fields.ResourceType);
        CanonicalEncoding.WriteString(buffer, fields.ResourceId);
        CanonicalEncoding.WriteInt64(buffer, fields.Sequence);
        CanonicalEncoding.WriteInt64(buffer, fields.OccurredOnUtcMicroseconds);
        CanonicalEncoding.WriteOptionalString(buffer, fields.BeforeSnapshot);
        CanonicalEncoding.WriteOptionalString(buffer, fields.AfterSnapshot);
        CanonicalEncoding.WriteOptionalString(buffer, fields.CorrelationId);
        CanonicalEncoding.WriteOptionalString(buffer, fields.ApprovalId);
        CanonicalEncoding.WriteInt32(buffer, (int)fields.Outcome);
        CanonicalEncoding.WriteOptionalString(buffer, fields.ErrorCode);
        CanonicalEncoding.WriteOptionalString(buffer, fields.ClientId);
        CanonicalEncoding.WriteOptionalString(buffer, fields.SessionId);
        CanonicalEncoding.WriteOptionalString(buffer, fields.ImpersonatorId);
        CanonicalEncoding.WriteOptionalString(buffer, fields.SourceService);
        CanonicalEncoding.WriteOptionalString(buffer, fields.IdempotencyKey);
        CanonicalEncoding.WriteOptionalString(buffer, fields.PreviousRecordHash);
    }
}
