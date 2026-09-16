using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SharedKernel.Cryptography.KeyDerivation;

/// <summary>Derives independent subkeys from one root key with HKDF-SHA256 (RFC 5869).</summary>
/// <remarks>
/// <para>
/// Give each purpose its own subkey instead of reusing one key everywhere: a subkey that leaks, or is misused by one
/// component, reveals nothing about the root key or any other subkey. A different purpose or context always yields
/// an unrelated key; the same inputs always yield the same key, so nothing needs to be stored.
/// </para>
/// <para>
/// The HKDF info is the purpose and context, each prefixed with its length, so no two distinct pairs produce the same
/// info. The root key must itself be uniformly random (a generated key, not a password); for passwords use
/// <see cref="Hashing.IOneWayHasher"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // One subkey per tenant for the "documents" feature.
/// byte[] tenantKey = SubkeyDerivation.DeriveKey(rootKey, "documents", Encoding.UTF8.GetBytes(tenantId));
/// </code>
/// </example>
public static class SubkeyDerivation
{
    /// <summary>The shortest root key accepted: 32 bytes.</summary>
    public const int MinimumRootKeyLength = 32;

    /// <summary>Derives a subkey into <paramref name="destination"/>.</summary>
    /// <param name="rootKey">The root key. At least <see cref="MinimumRootKeyLength"/> bytes.</param>
    /// <param name="purpose">
    /// A fixed name for what the subkey is for, such as <c>"documents"</c> or <c>"webhook-signing"</c>. Must not be
    /// empty.
    /// </param>
    /// <param name="context">Optional variable context, such as a tenant id. May be empty.</param>
    /// <param name="destination">Receives the subkey; its length is the subkey length, 16 to 64 bytes.</param>
    /// <exception cref="ArgumentException">An argument is out of range.</exception>
    public static void DeriveKey(
        ReadOnlySpan<byte> rootKey,
        string purpose,
        ReadOnlySpan<byte> context,
        Span<byte> destination)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);

        if (rootKey.Length < MinimumRootKeyLength)
        {
            throw new ArgumentException($"The root key must be at least {MinimumRootKeyLength} bytes.", nameof(rootKey));
        }

        if (destination.Length is < 16 or > 64)
        {
            throw new ArgumentException("The subkey must be 16 to 64 bytes.", nameof(destination));
        }

        int purposeLength = Encoding.UTF8.GetByteCount(purpose);
        byte[] info = new byte[4 + purposeLength + 4 + context.Length];
        Span<byte> span = info;
        BinaryPrimitives.WriteInt32BigEndian(span, purposeLength);
        Encoding.UTF8.GetBytes(purpose, span[4..]);
        BinaryPrimitives.WriteInt32BigEndian(span[(4 + purposeLength)..], context.Length);
        context.CopyTo(span[(8 + purposeLength)..]);

        HKDF.DeriveKey(HashAlgorithmName.SHA256, rootKey, destination, salt: default, info);
    }

    /// <summary>Derives a subkey and returns it.</summary>
    /// <param name="rootKey">The root key. At least <see cref="MinimumRootKeyLength"/> bytes.</param>
    /// <param name="purpose">A fixed name for what the subkey is for. Must not be empty.</param>
    /// <param name="context">Optional variable context, such as a tenant id. May be empty.</param>
    /// <param name="length">The subkey length, 16 to 64 bytes. Defaults to 32.</param>
    /// <returns>The subkey. Zero it with <see cref="CryptographicOperations.ZeroMemory"/> when no longer needed.</returns>
    /// <exception cref="ArgumentException">An argument is out of range.</exception>
    public static byte[] DeriveKey(ReadOnlySpan<byte> rootKey, string purpose, ReadOnlySpan<byte> context, int length = 32)
    {
        if (length is < 16 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The subkey must be 16 to 64 bytes.");
        }

        byte[] subkey = new byte[length];
        DeriveKey(rootKey, purpose, context, subkey);
        return subkey;
    }
}
