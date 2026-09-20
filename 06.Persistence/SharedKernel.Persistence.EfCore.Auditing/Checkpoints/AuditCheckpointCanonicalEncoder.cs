using System.Buffers;
using System.Text;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.EfCore.Auditing.Chain;

namespace SharedKernel.Persistence.EfCore.Auditing.Checkpoints;

/// <summary>
/// Canonical encoding of an <see cref="AuditChainCheckpoint"/>'s own fields — the bytes
/// <c>IAsymmetricSignatureService</c> signs and verifies. <see cref="AuditChainCheckpoint.Signature"/>
/// itself is obviously excluded (it is the output, not an input).
/// </summary>
internal static class AuditCheckpointCanonicalEncoder
{
    private static readonly byte[] DomainSeparator = Encoding.ASCII.GetBytes("AUDITCHECKPOINTv1");

    /// <summary>Encodes the fields of a checkpoint (new or already-persisted) that its signature covers.</summary>
    public static byte[] Encode(
        Guid id,
        Guid? tenantId,
        string resourceType,
        long sequence,
        string recordHash,
        long createdOnUtcMicroseconds,
        string signingKeyId)
    {
        var buffer = new ArrayBufferWriter<byte>(128);

        CanonicalEncoding.WriteBytes(buffer, DomainSeparator);
        CanonicalEncoding.WriteGuid(buffer, id);
        CanonicalEncoding.WriteOptionalGuid(buffer, tenantId);
        CanonicalEncoding.WriteString(buffer, resourceType);
        CanonicalEncoding.WriteInt64(buffer, sequence);
        CanonicalEncoding.WriteString(buffer, recordHash);
        CanonicalEncoding.WriteInt64(buffer, createdOnUtcMicroseconds);
        CanonicalEncoding.WriteString(buffer, signingKeyId);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Encodes an already-materialized <see cref="AuditChainCheckpoint"/>'s signed fields.</summary>
    public static byte[] Encode(AuditChainCheckpoint checkpoint) => Encode(
        checkpoint.Id,
        checkpoint.TenantId,
        checkpoint.ResourceType,
        checkpoint.Sequence,
        checkpoint.RecordHash,
        AuditTimestamp.ToUtcMicroseconds(checkpoint.CreatedOn),
        checkpoint.SigningKeyId);
}
