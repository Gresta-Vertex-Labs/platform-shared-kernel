using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SharedKernel.Cryptography;
using SharedKernel.Persistence.Abstractions.Repositories;

namespace SharedKernel.Persistence.EfCore.Concurrency;

/// <summary>The outcome of opening a version for one aggregate.</summary>
internal enum EntityVersionOpenResult
{
    /// <summary>The version is one of this aggregate; its row version was recovered.</summary>
    Opened,

    /// <summary>The version was sealed with a key this process does not know (for example one retired by a rotation).</summary>
    UnknownKey,

    /// <summary>The version is not one of this aggregate: it was sealed for another aggregate, or altered.</summary>
    NotThisAggregate,
}

/// <summary>
/// Seals PostgreSQL row versions into opaque <see cref="EntityVersion"/> tokens and opens them again.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Construction — encode-then-encipher with AES-256 as a single-block permutation.</strong> The 16-byte block
/// <c>xmin (4 bytes, big-endian) ‖ binding (12 bytes)</c> is enciphered with one AES-256 block operation under the
/// version key (an HKDF-SHA256 subkey of the service's root key, see <see cref="EntityVersionKeyRing"/>). The binding
/// is the first 12 bytes of <c>SHA-256("SharedKernel.Persistence.EntityVersion.Binding/1" ‖ LP(root entity type
/// name) ‖ LP(each primary-key value))</c>, <c>LP</c> being a 4-byte big-endian length prefix.
/// </para>
/// <para>
/// A block cipher is a pseudorandom permutation: without the key the ciphertext of a block reveals nothing about it,
/// and a ciphertext the key never produced deciphers to an unpredictable block. Opening therefore checks the 96-bit
/// binding (in fixed time): a token of another aggregate, an altered token or a forged one matches with probability
/// 2^-96 per attempt, so it is never taken for a version of this aggregate (Bellare–Rogaway, "Encode-then-encipher
/// encryption", 2000: enciphering redundant plaintext with a strong PRP is authenticated encryption). Enciphering
/// exactly one block needs no nonce, IV or padding, so the same version of the same aggregate always gives the same
/// token (deterministic, which <c>If-None-Match</c> needs; equality of versions is the only thing it reveals, by
/// design). This is the minimal standard construction for a message that fits one block: the deterministic AEAD modes
/// (AES-SIV, AES-GCM-SIV) generalize it to longer messages, and .NET ships neither.
/// </para>
/// <para>
/// <strong>Token (21 bytes, 28 characters of unpadded Base64Url):</strong> <c>0x01</c> (format) ‖ the key check value
/// (4 bytes, <see cref="EntityVersionKey.CheckValue"/>) ‖ the enciphered block (16 bytes). The check value finds the
/// key that sealed the token among the keys this process knows; it is an HKDF output of the root key independent of the
/// AES key.
/// </para>
/// <para>
/// A row version of 0 (an aggregate never saved) is never sealed: it is <see cref="EntityVersion.None"/>.
/// </para>
/// </remarks>
internal sealed class EntityVersionCodec
{
    /// <summary>The length of the binding inside the block.</summary>
    internal const int BindingLength = 12;

    private const byte FormatV1 = 0x01;
    private const int TokenLength = 21;
    private const int TextLength = 28;
    private const int BlockLength = 16;

    private static readonly byte[] BindingDomain = Encoding.ASCII.GetBytes("SharedKernel.Persistence.EntityVersion.Binding/1");

    /// <summary>Creates a codec over <paramref name="keys"/>.</summary>
    public EntityVersionCodec(EntityVersionKeyRing keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        Keys = keys;
    }

    /// <summary>A codec without keys: every seal and open throws <see cref="InvalidOperationException"/>.</summary>
    public static EntityVersionCodec Unconfigured { get; } = new(EntityVersionKeyRing.Unconfigured);

    /// <summary>The keys versions are sealed and opened with.</summary>
    internal EntityVersionKeyRing Keys { get; }

    /// <summary>Whether a key provider is registered, so versions can be issued.</summary>
    public bool IsConfigured => Keys.IsConfigured;

    /// <summary>Seals the row version of the aggregate tracked (or attached) by <paramref name="entry"/>.</summary>
    /// <param name="entry">The aggregate's entry; its primary key must be set.</param>
    /// <param name="rowVersion">The row version, not 0.</param>
    /// <returns>The opaque version.</returns>
    public EntityVersion Seal(EntityEntry entry, uint rowVersion)
    {
        Span<byte> binding = stackalloc byte[BindingLength];
        ComputeBinding(entry, binding);
        return Seal(rowVersion, binding);
    }

    /// <summary>Seals <paramref name="rowVersion"/> bound to <paramref name="binding"/> with the current key.</summary>
    /// <param name="rowVersion">The row version, not 0.</param>
    /// <param name="binding">The aggregate's binding, <see cref="BindingLength"/> bytes.</param>
    /// <returns>The opaque version.</returns>
    public EntityVersion Seal(uint rowVersion, ReadOnlySpan<byte> binding)
    {
        ArgumentOutOfRangeException.ThrowIfZero(rowVersion);
        if (binding.Length != BindingLength)
            throw new ArgumentException($"The binding must be {BindingLength} bytes.", nameof(binding));

        var key = Keys.GetCurrentKey();

        Span<byte> block = stackalloc byte[BlockLength];
        BinaryPrimitives.WriteUInt32BigEndian(block, rowVersion);
        binding.CopyTo(block[4..]);

        Span<byte> token = stackalloc byte[TokenLength];
        token[0] = FormatV1;
        BinaryPrimitives.WriteUInt32BigEndian(token[1..], key.CheckValue);
        EncryptBlock(key, block, token[5..]);

        Span<char> text = stackalloc char[TextLength];
        Base64Url.EncodeToChars(token, text);
        return EntityVersion.Parse(new string(text));
    }

    /// <summary>Opens <paramref name="version"/> for the aggregate of <paramref name="entry"/>.</summary>
    /// <param name="version">A version other than <see cref="EntityVersion.None"/>.</param>
    /// <param name="entry">The aggregate's entry, tracked or detached; its primary key must be set.</param>
    /// <param name="rowVersion">The row version, when <see cref="EntityVersionOpenResult.Opened"/>.</param>
    /// <returns>Whether the version is one of this aggregate.</returns>
    public EntityVersionOpenResult TryOpen(EntityVersion version, EntityEntry entry, out uint rowVersion)
    {
        Span<byte> binding = stackalloc byte[BindingLength];
        ComputeBinding(entry, binding);
        return TryOpen(version, binding, out rowVersion);
    }

    /// <summary>Opens <paramref name="version"/> for the aggregate whose binding is <paramref name="binding"/>.</summary>
    /// <param name="version">A version other than <see cref="EntityVersion.None"/>.</param>
    /// <param name="binding">The aggregate's binding, <see cref="BindingLength"/> bytes.</param>
    /// <param name="rowVersion">The row version, when <see cref="EntityVersionOpenResult.Opened"/>.</param>
    /// <returns>Whether the version is one of this aggregate.</returns>
    public EntityVersionOpenResult TryOpen(EntityVersion version, ReadOnlySpan<byte> binding, out uint rowVersion)
    {
        rowVersion = 0;
        if (binding.Length != BindingLength)
            throw new ArgumentException($"The binding must be {BindingLength} bytes.", nameof(binding));

        Span<byte> token = stackalloc byte[TokenLength];
        if (!TryReadToken(version, token))
            return EntityVersionOpenResult.NotThisAggregate;

        // The current key first: it is known to every process, and a rotation is picked up before looking.
        _ = Keys.GetCurrentKey();

        var checkValue = BinaryPrimitives.ReadUInt32BigEndian(token[1..]);
        var keyFound = false;
        Span<byte> block = stackalloc byte[BlockLength];
        var known = Keys.Known;
        for (var i = known.Count - 1; i >= 0; i--)
        {
            var key = known[i];
            if (key.CheckValue != checkValue)
                continue;

            keyFound = true;
            DecryptBlock(key, token[5..], block);
            if (FixedTimeComparison.AreEqual(block[4..], binding))
            {
                rowVersion = BinaryPrimitives.ReadUInt32BigEndian(block);
                return rowVersion == 0 ? EntityVersionOpenResult.NotThisAggregate : EntityVersionOpenResult.Opened;
            }
        }

        return keyFound ? EntityVersionOpenResult.NotThisAggregate : EntityVersionOpenResult.UnknownKey;
    }

    /// <summary>
    /// Writes the binding of the aggregate of <paramref name="entry"/>: its root entity type and primary key, the
    /// identity a version belongs to.
    /// </summary>
    /// <param name="entry">The entry, tracked or detached (key values are read from the entity).</param>
    /// <param name="destination">Receives <see cref="BindingLength"/> bytes.</param>
    /// <exception cref="InvalidOperationException">The entity type has no primary key, or a key value is not set.</exception>
    internal static void ComputeBinding(EntityEntry entry, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var entityType = entry.Metadata;
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"'{entityType.DisplayName()}' has no primary key, so it has no version.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BindingDomain);
        AppendLengthPrefixed(hash, Encoding.UTF8.GetBytes(entityType.GetRootType().Name));

        foreach (var property in primaryKey.Properties)
        {
            var value = entry.Property(property.Name).CurrentValue;
            var converter = property.GetValueConverter();
            var providerValue = converter is null || value is null ? value : converter.ConvertToProvider(value);

            AppendLengthPrefixed(hash, Canonicalize(providerValue)
                ?? throw new InvalidOperationException(
                    $"'{entityType.DisplayName()}.{property.Name}' is not set, so the aggregate has no version yet."));
        }

        Span<byte> digest = stackalloc byte[32];
        hash.GetHashAndReset(digest);
        digest[..BindingLength].CopyTo(destination);
    }

    private static byte[]? Canonicalize(object? value) => value switch
    {
        null => null,
        Guid guid => GuidBytes(guid),
        string text => Encoding.UTF8.GetBytes(text),
        byte[] bytes => bytes,
        DateTime dateTime => Encoding.UTF8.GetBytes(dateTime.ToString("O", CultureInfo.InvariantCulture)),
        DateTimeOffset dateTimeOffset => Encoding.UTF8.GetBytes(dateTimeOffset.ToString("O", CultureInfo.InvariantCulture)),
        IFormattable formattable => Encoding.UTF8.GetBytes(formattable.ToString(null, CultureInfo.InvariantCulture)),
        _ => Encoding.UTF8.GetBytes(value.ToString() ?? string.Empty),
    };

    private static byte[] GuidBytes(Guid guid)
    {
        var bytes = new byte[16];
        guid.TryWriteBytes(bytes, bigEndian: true, out _);
        return bytes;
    }

    private static void AppendLengthPrefixed(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }

    private static bool TryReadToken(EntityVersion version, Span<byte> token)
    {
        Span<char> text = stackalloc char[TextLength];
        return version.TryFormat(text, out var written, default, provider: null)
            && written == TextLength
            && Base64Url.TryDecodeFromChars(text, token, out var decoded)
            && decoded == TokenLength
            && token[0] == FormatV1;
    }

    // One AES block, enciphered directly (no mode, IV or padding: the input is exactly one block). A new instance per
    // call keeps the codec thread-safe; disposing it clears the key schedule.
    private static void EncryptBlock(EntityVersionKey key, ReadOnlySpan<byte> block, Span<byte> destination)
    {
        using var aes = Aes.Create();
        aes.SetKey(key.EncryptionKey);
        aes.EncryptEcb(block, destination, PaddingMode.None);
    }

    private static void DecryptBlock(EntityVersionKey key, ReadOnlySpan<byte> block, Span<byte> destination)
    {
        using var aes = Aes.Create();
        aes.SetKey(key.EncryptionKey);
        aes.DecryptEcb(block, destination, PaddingMode.None);
    }
}
