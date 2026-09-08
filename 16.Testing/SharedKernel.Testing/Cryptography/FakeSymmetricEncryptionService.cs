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
/// <para>
/// <b>BREAKING CHANGE (P-502/WO-081):</b> every member now takes a required
/// <c>byte[] associatedData</c> parameter, mirroring <c>01.Core</c>'s own required-AAD
/// <see cref="ISymmetricEncryptionService"/> shape (<c>SK.01.P491</c>) exactly. AAD is
/// <b>GENUINELY ENFORCED, never accepted-and-ignored</b>: every produced <see cref="EncryptedPayload.Tag"/>
/// is an <c>HMACSHA256</c> authentication tag computed over
/// <c>UTF8(KeyId) ++ Nonce ++ Ciphertext ++ AssociatedData</c> (in that order), keyed by the
/// resolved <see cref="CryptographicKey.Material"/> — the same key material used for the XOR
/// transform. <see cref="Decrypt(EncryptedPayload, byte[])"/>/<see cref="DecryptAsync(EncryptedPayload, byte[], CancellationToken)"/>
/// recompute that same tag from the supplied <c>associatedData</c> and compare it against
/// <see cref="EncryptedPayload.Tag"/> via <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>,
/// failing with the exact same <see cref="CryptographyErrorCodes.DecryptionFailed"/> shape
/// regardless of whether the ciphertext was tampered with, the wrong key was used, or the supplied
/// AAD does not match what was used at encryption time — mirroring <c>AesGcmEncryptionService</c>'s
/// own inability to distinguish those three causes. A fake that instead threaded
/// <c>associatedData</c> through unused would let every downstream domain's own AAD-binding test
/// pass vacuously — this is the single most valuable thing this migration prevents.
/// </para>
/// <para>
/// <see cref="EncryptedPayloads"/> widened from a bare payload list to
/// <c>IReadOnlyList&lt;(EncryptedPayload Payload, byte[] AssociatedData)&gt;</c> so a consuming test
/// can assert exactly which AAD bytes production code derived and passed for a given payload,
/// without inspecting ciphertext internals.
/// </para>
/// <para>
/// <see cref="SimulateDecryptFailure"/> is retained UNCHANGED — it forces a decrypt/verification
/// failure unconditionally, regardless of whether the supplied AAD would otherwise have matched,
/// coexisting with (and independent of) the new organic AAD-mismatch failure path above.
/// </para>
/// </remarks>
public sealed class FakeSymmetricEncryptionService : ISymmetricEncryptionService
{
    private const int NonceSize = 12;
    private const int TagSize = 32; // HMAC-SHA256 output size (256 bits).

    private readonly IEncryptionKeyProvider _keyProvider;
    private readonly ConcurrentQueue<(EncryptedPayload Payload, byte[] AssociatedData)> _encryptedPayloads = new();

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
    /// (and their async counterparts) should unconditionally simulate a tamper/wrong-key failure,
    /// without needing to hand-corrupt bytes. Defaults to <see langword="false"/>.
    /// </summary>
    public bool SimulateDecryptFailure { get; set; }

    /// <summary>
    /// Every payload ever produced by <see cref="Encrypt"/>/<see cref="EncryptToString"/> (and their
    /// async counterparts), paired with the exact associated-data bytes used to produce it,
    /// append-only.
    /// </summary>
    public IReadOnlyList<(EncryptedPayload Payload, byte[] AssociatedData)> EncryptedPayloads => _encryptedPayloads.ToArray();

    /// <inheritdoc />
    public EncryptedPayload Encrypt(byte[] plaintext, byte[] associatedData)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(associatedData);

        // Genuinely non-blocking against this fake's own synchronously-completing
        // IEncryptionKeyProvider implementations — see class remarks.
        CryptographicKey key = _keyProvider.GetCurrentKeyAsync().GetAwaiter().GetResult();
        return EncryptCore(plaintext, associatedData, key);
    }

    /// <inheritdoc />
    public async ValueTask<EncryptedPayload> EncryptAsync(byte[] plaintext, byte[] associatedData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(associatedData);

        CryptographicKey key = await _keyProvider.GetCurrentKeyAsync(ct).ConfigureAwait(false);
        return EncryptCore(plaintext, associatedData, key);
    }

    /// <inheritdoc />
    public SharedKernel.Primitives.Results.Result<byte[]> Decrypt(EncryptedPayload payload, byte[] associatedData)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(associatedData);

        if (SimulateDecryptFailure)
        {
            return SimulatedDecryptFailure();
        }

        // Genuinely non-blocking against this fake's own synchronously-completing
        // IEncryptionKeyProvider implementations — see class remarks.
        CryptographicKey? key = _keyProvider.GetKeyAsync(payload.KeyId).GetAwaiter().GetResult();
        return DecryptCore(payload, associatedData, key);
    }

    /// <inheritdoc />
    public async ValueTask<SharedKernel.Primitives.Results.Result<byte[]>> DecryptAsync(EncryptedPayload payload, byte[] associatedData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(associatedData);

        if (SimulateDecryptFailure)
        {
            return SimulatedDecryptFailure();
        }

        CryptographicKey? key = await _keyProvider.GetKeyAsync(payload.KeyId, ct).ConfigureAwait(false);
        return DecryptCore(payload, associatedData, key);
    }

    /// <inheritdoc />
    public string EncryptToString(string plaintext, byte[] associatedData)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(associatedData);

        EncryptedPayload payload = Encrypt(Encoding.UTF8.GetBytes(plaintext), associatedData);
        return Pack(payload);
    }

    /// <inheritdoc />
    public async ValueTask<string> EncryptToStringAsync(string plaintext, byte[] associatedData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(associatedData);

        EncryptedPayload payload = await EncryptAsync(Encoding.UTF8.GetBytes(plaintext), associatedData, ct).ConfigureAwait(false);
        return Pack(payload);
    }

    /// <inheritdoc />
    public SharedKernel.Primitives.Results.Result<string> DecryptToString(string encoded, byte[] associatedData)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        ArgumentNullException.ThrowIfNull(associatedData);

        if (!TryUnpack(encoded, out EncryptedPayload? payload))
        {
            return SharedKernel.Primitives.Results.Result<string>.Failure(
                SharedKernel.Primitives.Errors.Error.Unexpected(
                    CryptographyErrorCodes.MalformedPayload,
                    "The supplied string is not a valid encrypted payload."));
        }

        SharedKernel.Primitives.Results.Result<byte[]> result = Decrypt(payload, associatedData);
        return result.IsSuccess
            ? SharedKernel.Primitives.Results.Result<string>.Success(Encoding.UTF8.GetString(result.Value))
            : SharedKernel.Primitives.Results.Result<string>.Failure(result.Error);
    }

    /// <inheritdoc />
    public async ValueTask<SharedKernel.Primitives.Results.Result<string>> DecryptToStringAsync(string encoded, byte[] associatedData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        ArgumentNullException.ThrowIfNull(associatedData);

        if (!TryUnpack(encoded, out EncryptedPayload? payload))
        {
            return SharedKernel.Primitives.Results.Result<string>.Failure(
                SharedKernel.Primitives.Errors.Error.Unexpected(
                    CryptographyErrorCodes.MalformedPayload,
                    "The supplied string is not a valid encrypted payload."));
        }

        SharedKernel.Primitives.Results.Result<byte[]> result = await DecryptAsync(payload, associatedData, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? SharedKernel.Primitives.Results.Result<string>.Success(Encoding.UTF8.GetString(result.Value))
            : SharedKernel.Primitives.Results.Result<string>.Failure(result.Error);
    }

    private EncryptedPayload EncryptCore(byte[] plaintext, byte[] associatedData, CryptographicKey key)
    {
        byte[] ciphertext = Xor(plaintext, key.Material);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = ComputeTag(key.Id, nonce, ciphertext, associatedData, key.Material);

        var payload = new EncryptedPayload(key.Id, nonce, ciphertext, tag);
        _encryptedPayloads.Enqueue((payload, associatedData));
        return payload;
    }

    private static SharedKernel.Primitives.Results.Result<byte[]> DecryptCore(EncryptedPayload payload, byte[] associatedData, CryptographicKey? key)
    {
        if (key is null)
        {
            return SharedKernel.Primitives.Results.Result<byte[]>.Failure(
                SharedKernel.Primitives.Errors.Error.Unexpected(
                    CryptographyErrorCodes.UnknownKeyId,
                    $"No encryption key registered for key id '{payload.KeyId}'."));
        }

        byte[] expectedTag = ComputeTag(payload.KeyId, payload.Nonce, payload.Ciphertext, associatedData, key.Material);
        if (!CryptographicOperations.FixedTimeEquals(expectedTag, payload.Tag))
        {
            return SharedKernel.Primitives.Results.Result<byte[]>.Failure(
                SharedKernel.Primitives.Errors.Error.Unexpected(
                    CryptographyErrorCodes.DecryptionFailed,
                    "Decryption failed: the payload may have been tampered with, the wrong key was used, or the associated data does not match what was supplied at encryption time."));
        }

        return SharedKernel.Primitives.Results.Result<byte[]>.Success(Xor(payload.Ciphertext, key.Material));
    }

    private static SharedKernel.Primitives.Results.Result<byte[]> SimulatedDecryptFailure() =>
        SharedKernel.Primitives.Results.Result<byte[]>.Failure(
            SharedKernel.Primitives.Errors.Error.Unexpected(
                CryptographyErrorCodes.DecryptionFailed,
                "Decryption failed: the payload may have been tampered with or the wrong key was used."));

    /// <summary>
    /// Computes the HMAC-SHA256 authentication tag over
    /// <c>UTF8(keyId) ++ nonce ++ ciphertext ++ associatedData</c>, keyed by <paramref name="keyMaterial"/>
    /// — this is what makes AAD a genuine authentication input rather than an accepted-and-ignored
    /// parameter. See class remarks.
    /// </summary>
    private static byte[] ComputeTag(string keyId, byte[] nonce, byte[] ciphertext, byte[] associatedData, byte[] keyMaterial)
    {
        byte[] keyIdBytes = Encoding.UTF8.GetBytes(keyId);
        var buffer = new byte[keyIdBytes.Length + nonce.Length + ciphertext.Length + associatedData.Length];
        int offset = 0;

        keyIdBytes.CopyTo(buffer, offset);
        offset += keyIdBytes.Length;
        nonce.CopyTo(buffer, offset);
        offset += nonce.Length;
        ciphertext.CopyTo(buffer, offset);
        offset += ciphertext.Length;
        associatedData.CopyTo(buffer, offset);

        return HMACSHA256.HashData(keyMaterial, buffer);
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
