using System.Buffers.Binary;
using System.Text;

namespace SharedKernel.Persistence.EfCore.Encryption.Crypto;

/// <summary>Builds the canonical AES-GCM associated data (AAD) for one encrypted value.</summary>
/// <remarks>
/// <para>
/// <strong>Format:</strong> the 6-byte tag <c>"SKENC2"</c>, then <c>purpose</c>, <c>primaryKey</c> and
/// <c>tenantId</c>, each as a 4-byte big-endian length followed by its bytes, so no combination of inputs can alias
/// another. A non-tenanted row has a zero-length tenant component.
/// </para>
/// <para>
/// A ciphertext copied into another row, another tenant or another encrypted column fails authentication instead
/// of decrypting as the wrong value. "Another column" holds because the model convention requires every purpose to
/// be unique across the whole model, so the purpose identifies the column; the physical table and column names are
/// never bound, so renaming them never invalidates stored data.
/// </para>
/// </remarks>
internal static class AssociatedDataBuilder
{
    private static readonly byte[] DomainSeparator = Encoding.ASCII.GetBytes("SKENC2");

    /// <summary>Builds the AAD for one value.</summary>
    /// <param name="purpose">The property's purpose label.</param>
    /// <param name="primaryKey">The row's canonical primary key bytes, see <see cref="PrimaryKeyCanonicalizer"/>.</param>
    /// <param name="tenantId">The row's tenant id, or <see langword="null"/> when the entity is not tenanted.</param>
    /// <returns>The associated data.</returns>
    public static byte[] Build(string purpose, ReadOnlySpan<byte> primaryKey, Guid? tenantId)
    {
        var purposeBytes = Encoding.UTF8.GetBytes(purpose);
        Span<byte> tenantBytes = stackalloc byte[16];
        var tenantLength = 0;
        if (tenantId is { } id)
        {
            id.TryWriteBytes(tenantBytes);
            tenantLength = 16;
        }

        var result = new byte[DomainSeparator.Length + 12 + purposeBytes.Length + primaryKey.Length + tenantLength];
        var span = result.AsSpan();

        DomainSeparator.CopyTo(span);
        span = span[DomainSeparator.Length..];
        span = WriteLengthPrefixed(span, purposeBytes);
        span = WriteLengthPrefixed(span, primaryKey);
        WriteLengthPrefixed(span, tenantBytes[..tenantLength]);

        return result;
    }

    private static Span<byte> WriteLengthPrefixed(Span<byte> destination, ReadOnlySpan<byte> value)
    {
        BinaryPrimitives.WriteInt32BigEndian(destination, value.Length);
        destination = destination[4..];
        value.CopyTo(destination);
        return destination[value.Length..];
    }
}
