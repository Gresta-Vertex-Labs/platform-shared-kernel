using System.Buffers.Binary;
using System.Text;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Builds the canonical AES-GCM associated data (AAD) for one encrypted row+column.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Format:</strong> a fixed 6-byte domain-separator tag (<c>"SKENC1"</c>), followed by each of
/// <c>purpose</c>, <c>primaryKey</c> and <c>tenantId</c> as a 4-byte big-endian length prefix plus its bytes — never
/// delimiter-joined, so no combination of inputs can alias another. <c>tenantId</c> is zero-length (a 4-byte
/// length prefix of <c>0</c>, no payload) for a non-tenanted entity or one that opted out of per-row tenant
/// binding, which is itself part of the authenticated bytes — a tenanted row's ciphertext can never be replayed
/// as if it belonged to a non-tenanted property, or vice versa.
/// </para>
/// <para>
/// Binding to <c>purpose</c> (a stable application-level label) and the row's primary key means a ciphertext
/// copied into another row, another column, or another tenant fails AES-GCM authentication instead of silently
/// "decrypting" as the wrong value. Binding never depends on the physical table, column or schema name — renaming
/// any of those never invalidates existing ciphertext; renaming <c>purpose</c> itself does, since it is one of the
/// bound components.
/// </para>
/// </remarks>
internal static class AssociatedDataBuilder
{
    private static readonly byte[] DomainSeparator = Encoding.ASCII.GetBytes("SKENC1");

    /// <summary>Builds the AAD for one property on one row.</summary>
    /// <param name="purpose">The property's stable purpose label, e.g. <c>"customer.email"</c>.</param>
    /// <param name="primaryKey">The row's canonical primary key bytes — see <see cref="PrimaryKeyCanonicalizer"/>.</param>
    /// <param name="tenantId">The row's tenant id, or <see langword="null"/> when the entity is not tenant-bound.</param>
    /// <returns>The associated data bytes.</returns>
    public static byte[] Build(string purpose, ReadOnlySpan<byte> primaryKey, Guid? tenantId)
    {
        var purposeBytes = Encoding.UTF8.GetBytes(purpose);
        var tenantBytes = tenantId is { } id ? id.ToByteArray() : [];

        var total = DomainSeparator.Length
            + 4 + purposeBytes.Length
            + 4 + primaryKey.Length
            + 4 + tenantBytes.Length;

        var result = new byte[total];
        var span = result.AsSpan();

        DomainSeparator.CopyTo(span);
        span = span[DomainSeparator.Length..];

        span = WriteLengthPrefixed(span, purposeBytes);
        span = WriteLengthPrefixed(span, primaryKey);
        WriteLengthPrefixed(span, tenantBytes);

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
