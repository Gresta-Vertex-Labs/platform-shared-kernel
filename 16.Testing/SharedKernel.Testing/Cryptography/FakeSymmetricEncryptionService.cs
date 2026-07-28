using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// In-memory test double for <see cref="ISymmetricEncryptionService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Uses a deterministic, NON-cryptographic reversible transform (byte-XOR, repeating the resolved
/// key's material across the plaintext length) instead of real AES-GCM.
/// </para>
/// <para>
/// <b>THIS IS NEVER SECURE. TEST-ONLY.</b> Never a substitute for <c>AesGcmEncryptionService</c> in
/// anything security-sensitive — the XOR transform provides no confidentiality or integrity
/// guarantee whatsoever. Wiring it into a production DI container by accident would make encrypted
/// payloads trivially reversible by anyone who can see the ciphertext and infer the key length.
/// </para>
/// </remarks>
public sealed class FakeSymmetricEncryptionService : ISymmetricEncryptionService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IEncryptionKeyProvider _keyProvider;
    private readonly ConcurrentQueue<EncryptedPayload> _encryptedPayloads = new();

    /// <summary>
    /// Initialises a new <see cref="FakeSymmetricEncryptionService"/>.
    /// </summary>
    /// <param name="keyProvider">
    /// The key provider to resolve key material from. When omitted, defaults to an
    /// internally-owned <see cref="FakeEncryptionKeyProvider"/> for zero-config convenience.
    /// Accepts any <see cref="IEncryptionKeyProvider"/>, including a real one, mirroring
    /// <c>AesGcmEncryptionService</c>'s exact constructor shape for DI-swap parity.
    /// </param>
    public FakeSymmetricEncryptionService(IEncryptionKeyProvider? keyProvider = null)
    {
        _keyProvider = keyProvider ?? new FakeEncryptionKeyProvider();
    }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Decrypt"/>/<see cref="DecryptToString"/>
    /// should unconditionally simulate a tamper/wrong-key failure, without needing to hand-corrupt
    /// bytes. Defaults to <see langword="false"/>.
    /// </summary>
    public bool SimulateDecryptFailure { get; set; }

    /// <summary>Every payload ever produced by <see cref="Encrypt"/>/<see cref="EncryptToString"/>, append-only.</summary>
    public IReadOnlyList<EncryptedPayload> EncryptedPayloads => _encryptedPayloads.ToArray();

    /// <inheritdoc />
    public EncryptedPayload Encrypt(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        CryptographicKey key = _keyProvider.GetCurrentKey();
        byte[] ciphertext = Xor(plaintext, key.Material);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];

        var payload = new EncryptedPayload(key.Id, nonce, ciphertext, tag);
        _encryptedPayloads.Enqueue(payload);
        return payload;
    }

    /// <inheritdoc />
    public SharedKernel.Primitives.Results.Result<byte[]> Decrypt(EncryptedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (SimulateDecryptFailure)
        {
            return SharedKernel.Primitives.Results.Result<byte[]>.Failure(
                SharedKernel.Primitives.Errors.Error.Unexpected(
                    CryptographyErrorCodes.DecryptionFailed,
                    "Decryption failed: the payload may have been tampered with or the wrong key was used."));
        }

        CryptographicKey? key = _keyProvider.GetKey(payload.KeyId);
        if (key is null)
        {
            return SharedKernel.Primitives.Results.Result<byte[]>.Failure(
                SharedKernel.Primitives.Errors.Error.Unexpected(
                    CryptographyErrorCodes.UnknownKeyId,
                    $"No encryption key registered for key id '{payload.KeyId}'."));
        }

        return SharedKernel.Primitives.Results.Result<byte[]>.Success(Xor(payload.Ciphertext, key.Material));
    }

    /// <inheritdoc />
    public string EncryptToString(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        EncryptedPayload payload = Encrypt(Encoding.UTF8.GetBytes(plaintext));
        return Pack(payload);
    }

    /// <inheritdoc />
    public SharedKernel.Primitives.Results.Result<string> DecryptToString(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        if (!TryUnpack(encoded, out EncryptedPayload? payload))
        {
            return SharedKernel.Primitives.Results.Result<string>.Failure(
                SharedKernel.Primitives.Errors.Error.Unexpected(
                    CryptographyErrorCodes.MalformedPayload,
                    "The supplied string is not a valid encrypted payload."));
        }

        SharedKernel.Primitives.Results.Result<byte[]> result = Decrypt(payload);
        return result.IsSuccess
            ? SharedKernel.Primitives.Results.Result<string>.Success(Encoding.UTF8.GetString(result.Value))
            : SharedKernel.Primitives.Results.Result<string>.Failure(result.Error);
    }

    private static byte[] Xor(byte[] data, byte[] key)
    {
        var output = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            output[i] = (byte)(data[i] ^ key[i % key.Length]);
        }

        return output;
    }

    /// <summary>
    /// Packs <c>KeyId</c> length + UTF-8 KeyId bytes + Nonce + Tag + Ciphertext into a single Base64
    /// string — the same wire shape <c>AesGcmEncryptionService</c> uses, for realism (Nonce and Tag
    /// are fixed-size, so only the variable-length KeyId needs an explicit length prefix).
    /// </summary>
    private static string Pack(EncryptedPayload payload)
    {
        byte[] keyIdBytes = Encoding.UTF8.GetBytes(payload.KeyId);
        int length = 2 + keyIdBytes.Length + NonceSize + TagSize + payload.Ciphertext.Length;
        byte[] buffer = new byte[length];
        Span<byte> span = buffer;

        BinaryPrimitives.WriteUInt16BigEndian(span[0..2], (ushort)keyIdBytes.Length);
        int offset = 2;
        keyIdBytes.CopyTo(span[offset..]);
        offset += keyIdBytes.Length;
        payload.Nonce.CopyTo(span[offset..]);
        offset += NonceSize;
        payload.Tag.CopyTo(span[offset..]);
        offset += TagSize;
        payload.Ciphertext.CopyTo(span[offset..]);

        return Convert.ToBase64String(buffer);
    }

    private static bool TryUnpack(string encoded, [NotNullWhen(true)] out EncryptedPayload? payload)
    {
        payload = null;

        byte[] buffer;
        try
        {
            buffer = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return false;
        }

        if (buffer.Length < 2)
        {
            return false;
        }

        ReadOnlySpan<byte> span = buffer;
        ushort keyIdLength = BinaryPrimitives.ReadUInt16BigEndian(span[0..2]);
        int offset = 2;
        int minimumLength = offset + keyIdLength + NonceSize + TagSize;
        if (buffer.Length < minimumLength)
        {
            return false;
        }

        string keyId = Encoding.UTF8.GetString(span[offset..(offset + keyIdLength)]);
        offset += keyIdLength;
        byte[] nonce = span[offset..(offset + NonceSize)].ToArray();
        offset += NonceSize;
        byte[] tag = span[offset..(offset + TagSize)].ToArray();
        offset += TagSize;
        byte[] ciphertext = span[offset..].ToArray();

        payload = new EncryptedPayload(keyId, nonce, ciphertext, tag);
        return true;
    }
}
