using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// Encrypts and decrypts byte payloads using AES-256-GCM (<see cref="AesGcm"/>) — an
/// authenticated (AEAD) cipher only. A fresh random 96-bit nonce is generated for every
/// encrypt call and is never reused.
/// </summary>
/// <remarks>
/// The synchronous <see cref="Encrypt(byte[])"/>/<see cref="Decrypt(EncryptedPayload)"/>/
/// <see cref="EncryptToString(string)"/>/<see cref="DecryptToString(string)"/> members and their
/// asynchronous counterparts share the exact same cryptographic core (<c>EncryptCore</c>/
/// <c>DecryptCore</c>) — the only difference between a sync and an async call is how the
/// <see cref="IEncryptionKeyProvider"/> result is awaited. This guarantees byte-identical
/// ciphertext/plaintext behavior between the two call shapes for the same input.
/// </remarks>
public sealed class AesGcmEncryptionService : ISymmetricEncryptionService
{
    private const int NonceSize = 12; // 96 bits
    private const int TagSize = 16; // 128 bits

    private readonly IEncryptionKeyProvider _keyProvider;

    /// <summary>Creates a new <see cref="AesGcmEncryptionService"/>.</summary>
    /// <param name="keyProvider">Resolves the current and historical key material.</param>
    public AesGcmEncryptionService(IEncryptionKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        _keyProvider = keyProvider;
    }

    /// <inheritdoc />
    public EncryptedPayload Encrypt(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        // GENUINELY NON-BLOCKING when the provider resolves synchronously; blocks a real thread
        // when the provider is a network-bound KMS call — see the interface XML docs.
        CryptographicKey key = _keyProvider.GetCurrentKeyAsync().GetAwaiter().GetResult();
        return EncryptCore(plaintext, key);
    }

    /// <inheritdoc />
    public async ValueTask<EncryptedPayload> EncryptAsync(byte[] plaintext, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        CryptographicKey key = await _keyProvider.GetCurrentKeyAsync(ct).ConfigureAwait(false);
        return EncryptCore(plaintext, key);
    }

    /// <inheritdoc />
    public Result<byte[]> Decrypt(EncryptedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // GENUINELY NON-BLOCKING when the provider resolves synchronously; blocks a real thread
        // when the provider is a network-bound KMS call — see the interface XML docs.
        CryptographicKey? key = _keyProvider.GetKeyAsync(payload.KeyId).GetAwaiter().GetResult();
        return DecryptCore(payload, key);
    }

    /// <inheritdoc />
    public async ValueTask<Result<byte[]>> DecryptAsync(EncryptedPayload payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        CryptographicKey? key = await _keyProvider.GetKeyAsync(payload.KeyId, ct).ConfigureAwait(false);
        return DecryptCore(payload, key);
    }

    /// <inheritdoc />
    public string EncryptToString(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        EncryptedPayload payload = Encrypt(Encoding.UTF8.GetBytes(plaintext));
        return Pack(payload);
    }

    /// <inheritdoc />
    public async ValueTask<string> EncryptToStringAsync(string plaintext, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        EncryptedPayload payload = await EncryptAsync(Encoding.UTF8.GetBytes(plaintext), ct).ConfigureAwait(false);
        return Pack(payload);
    }

    /// <inheritdoc />
    public Result<string> DecryptToString(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        if (!TryUnpack(encoded, out EncryptedPayload? payload))
        {
            return Error.Unexpected(
                CryptographyErrorCodes.MalformedPayload,
                "The supplied string is not a valid encrypted payload.");
        }

        Result<byte[]> result = Decrypt(payload);
        return result.IsSuccess
            ? Encoding.UTF8.GetString(result.Value)
            : result.Error;
    }

    /// <inheritdoc />
    public async ValueTask<Result<string>> DecryptToStringAsync(string encoded, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        if (!TryUnpack(encoded, out EncryptedPayload? payload))
        {
            return Error.Unexpected(
                CryptographyErrorCodes.MalformedPayload,
                "The supplied string is not a valid encrypted payload.");
        }

        Result<byte[]> result = await DecryptAsync(payload, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Encoding.UTF8.GetString(result.Value)
            : result.Error;
    }

    /// <summary>
    /// The pure, synchronous AES-256-GCM encryption core shared by both the sync and async
    /// public members — performs no I/O and no key resolution of its own.
    /// </summary>
    private static EncryptedPayload EncryptCore(byte[] plaintext, CryptographicKey key)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using var aesGcm = new AesGcm(key.Material, TagSize);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        return new EncryptedPayload(key.Id, nonce, ciphertext, tag);
    }

    /// <summary>
    /// The pure, synchronous AES-256-GCM decryption core shared by both the sync and async
    /// public members — performs no I/O and no key resolution of its own.
    /// </summary>
    private static Result<byte[]> DecryptCore(EncryptedPayload payload, CryptographicKey? key)
    {
        if (key is null)
        {
            return Error.Unexpected(
                CryptographyErrorCodes.UnknownKeyId,
                $"No encryption key registered for key id '{payload.KeyId}'.");
        }

        byte[] plaintext = new byte[payload.Ciphertext.Length];

        try
        {
            using var aesGcm = new AesGcm(key.Material, TagSize);
            aesGcm.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, plaintext);
        }
        catch (CryptographicException)
        {
            return Error.Unexpected(
                CryptographyErrorCodes.DecryptionFailed,
                "Decryption failed: the payload may have been tampered with or the wrong key was used.");
        }

        return plaintext;
    }

    /// <summary>
    /// Packs <c>KeyId</c> length + UTF-8 KeyId bytes + Nonce + Tag + Ciphertext into a single
    /// Base64 string. Nonce and Tag are fixed-size (12 and 16 bytes respectively), so only the
    /// variable-length KeyId needs an explicit length prefix.
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
