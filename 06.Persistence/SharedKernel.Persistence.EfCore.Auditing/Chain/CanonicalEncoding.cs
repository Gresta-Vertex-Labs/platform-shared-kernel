using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace SharedKernel.Persistence.EfCore.Auditing.Chain;

/// <summary>
/// Length-prefixed binary primitives shared by <see cref="AuditRecordHasher"/> (record hashing) and
/// the checkpoint canonical encoder (checkpoint signing) — the same encoding discipline (explicit
/// presence flags so <see langword="null"/> and <see cref="string.Empty"/> never collide, fixed-width
/// integers, big-endian) applied consistently everywhere this package turns structured fields into
/// bytes to hash or sign.
/// </summary>
internal static class CanonicalEncoding
{
    public static void WriteInt32(ArrayBufferWriter<byte> buffer, int value)
    {
        var span = buffer.GetSpan(4);
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        buffer.Advance(4);
    }

    public static void WriteInt64(ArrayBufferWriter<byte> buffer, long value)
    {
        var span = buffer.GetSpan(8);
        BinaryPrimitives.WriteInt64BigEndian(span, value);
        buffer.Advance(8);
    }

    public static void WriteGuid(ArrayBufferWriter<byte> buffer, Guid value)
    {
        var span = buffer.GetSpan(16);
        value.TryWriteBytes(span);
        buffer.Advance(16);
    }

    public static void WriteOptionalGuid(ArrayBufferWriter<byte> buffer, Guid? value)
    {
        WritePresence(buffer, value.HasValue);
        if (value is { } v)
            WriteGuid(buffer, v);
    }

    public static void WritePresence(ArrayBufferWriter<byte> buffer, bool present)
    {
        var span = buffer.GetSpan(1);
        span[0] = present ? (byte)1 : (byte)0;
        buffer.Advance(1);
    }

    public static void WriteBytes(ArrayBufferWriter<byte> buffer, ReadOnlySpan<byte> value)
    {
        WriteInt32(buffer, value.Length);
        var span = buffer.GetSpan(value.Length);
        value.CopyTo(span);
        buffer.Advance(value.Length);
    }

    public static void WriteString(ArrayBufferWriter<byte> buffer, string value) =>
        WriteBytes(buffer, Encoding.UTF8.GetBytes(value));

    public static void WriteOptionalString(ArrayBufferWriter<byte> buffer, string? value)
    {
        WritePresence(buffer, value is not null);
        if (value is not null)
            WriteString(buffer, value);
    }
}
