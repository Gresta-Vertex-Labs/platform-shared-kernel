using System.Buffers.Binary;
using System.Text;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Messaging.MassTransit.Serialization;

/// <summary>
/// Packs/unpacks a <see cref="SharedKernel.Cryptography.Symmetric.EncryptedPayload"/> to/from a
/// single flat byte array suitable for a MassTransit message body.
/// </summary>
/// <remarks>
/// Distinct from <c>ISymmetricEncryptionService.EncryptToString</c>'s Base64-packed string wire
/// format — that convenience method targets string-shaped call sites (headers, query strings); this
/// codec targets the raw binary message body a MassTransit <c>IMessageSerializer</c> produces, so it
/// skips the Base64 inflation entirely. Every component (KeyId, Nonce, Tag) is explicitly
/// length-prefixed rather than assumed fixed-size, since <see cref="EncryptedPayload"/> itself makes
/// no such guarantee — that is an implementation detail of the concrete
/// <c>ISymmetricEncryptionService</c> in use.
/// </remarks>
internal static class EncryptedPayloadWireCodec
{
    private const int LengthPrefixSize = sizeof(int);

    /// <summary>Encodes <paramref name="payload"/> into a single flat byte array.</summary>
    /// <param name="payload">The encrypted payload to encode.</param>
    /// <returns>The encoded bytes.</returns>
    internal static byte[] Encode(EncryptedPayload payload)
    {
        byte[] keyIdBytes = Encoding.UTF8.GetBytes(payload.KeyId);

        int length = (LengthPrefixSize * 3)
            + keyIdBytes.Length
            + payload.Nonce.Length
            + payload.Tag.Length
            + payload.Ciphertext.Length;

        byte[] buffer = new byte[length];
        Span<byte> span = buffer;
        int offset = 0;

        offset = WriteSegment(span, offset, keyIdBytes);
        offset = WriteSegment(span, offset, payload.Nonce);
        offset = WriteSegment(span, offset, payload.Tag);
        payload.Ciphertext.CopyTo(span[offset..]);

        return buffer;
    }

    /// <summary>
    /// Decodes a byte array previously produced by <see cref="Encode"/> back into an
    /// <see cref="EncryptedPayload"/>.
    /// </summary>
    /// <param name="data">The bytes to decode.</param>
    /// <returns>The decoded <see cref="EncryptedPayload"/>.</returns>
    /// <exception cref="FormatException">
    /// Thrown when <paramref name="data"/> is not a valid encoding produced by <see cref="Encode"/> —
    /// deliberately loud, never a silently wrong result, so a payload-transform configuration
    /// mismatch is caught rather than misinterpreted.
    /// </exception>
    internal static EncryptedPayload Decode(byte[] data)
    {
        ReadOnlySpan<byte> span = data;
        int offset = 0;

        byte[] keyIdBytes = ReadSegment(span, ref offset);
        byte[] nonce = ReadSegment(span, ref offset);
        byte[] tag = ReadSegment(span, ref offset);
        byte[] ciphertext = span[offset..].ToArray();

        return new EncryptedPayload(Encoding.UTF8.GetString(keyIdBytes), nonce, ciphertext, tag);
    }

    private static int WriteSegment(Span<byte> span, int offset, byte[] segment)
    {
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset, LengthPrefixSize), segment.Length);
        offset += LengthPrefixSize;
        segment.CopyTo(span[offset..]);
        return offset + segment.Length;
    }

    private static byte[] ReadSegment(ReadOnlySpan<byte> span, ref int offset)
    {
        if (offset + LengthPrefixSize > span.Length)
            throw new FormatException("Malformed encrypted payload wire format: truncated length prefix.");

        int length = BinaryPrimitives.ReadInt32BigEndian(span.Slice(offset, LengthPrefixSize));
        offset += LengthPrefixSize;

        if (length < 0 || offset + length > span.Length)
            throw new FormatException("Malformed encrypted payload wire format: truncated segment.");

        byte[] segment = span.Slice(offset, length).ToArray();
        offset += length;
        return segment;
    }
}
