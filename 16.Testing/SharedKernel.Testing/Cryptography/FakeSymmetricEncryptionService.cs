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
/// <b>BREAKING CHANGE (P-450/WO-068):</b> the constructor-injected <see cref="IEncryptionKeyProvider"/>
/// is now resolved via its async members only (P-446/WO-068). The synchronous
/// <see cref="Encrypt(byte[])"/>/<see cref="Decrypt(EncryptedPayload)"/> members below bridge onto
/// <see cref="IEncryptionKeyProvider.GetCurrentKeyAsync(CancellationToken)"/>/
/// <see cref="IEncryptionKeyProvider.GetKeyAsync(string, CancellationToken)"/> via
/// <c>.GetAwaiter().GetResult()</c> — genuinely non-blocking against this fake's own
/// synchronously-completing <c>IEncryptionKeyProvider</c> implementations (e.g.
/// <see cref="FakeEncryptionKeyProvider"/>), mirroring <c>AesGcmEncryptionService</c>'s exact
/// bridging pattern. This fake's own public surface is unchanged and additive-only — the new async
/// members (<see cref="EncryptAsync"/>/<see cref="DecryptAsync"/>/<see cref="EncryptToStringAsync"/>/
/// <see cref="DecryptToStringAsync"/>) sit alongside the retained synchronous ones, exactly as
/// <c>ISymmetricEncryptionService</c> itself does.
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
    /// (and their async counterparts) should unconditionally simulate a tamper/wrong-key failure,
    /// without needing to hand-corrupt bytes. Defaults to <see langword="false"/>.
    /// </summary>
    public bool SimulateDecryptFailure { get; set; }

    /// <summary>Every payload ever produced by <see cref="Encrypt"/>/<see cref="EncryptToString"/> (and their async counterparts), append-only.</summary>
    public IReadOnlyList<EncryptedPayload> EncryptedPayloads => _encryptedPayloads.ToArray();

    /// <inheritdoc />
    public EncryptedPayload Encrypt(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        // Genuinely non-blocking against this fake's own synchronously-completing
        // IEncryptionKeyProvider implementations — see class remarks.
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
    public SharedKernel.Primitives.Results.Result<byte[]> Decrypt(EncryptedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (SimulateDecryptFailure)
        {
            return SimulatedDecryptFailure();
        }

        // Genuinely non-blocking against this fake's own synchronously-completing
        // IEncryptionKeyProvider implementations — see class remarks.
        CryptographicKey? key = _keyProvider.GetKeyAsync(payload.KeyId).GetAwaiter().GetResult();
        return DecryptCore(payload, key);
    }

    /// <inheritdoc />
    public async ValueTask<SharedKernel.Primitives.Results.Result<byte[]>> DecryptAsync(EncryptedPayload payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (SimulateDecryptFailure)
        {
            return SimulatedDecryptFailure();
        }

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

    /// <inheritdoc />
    public async ValueTask<SharedKernel.Primitives.Results.Result<string>> DecryptToStringAsync(string encoded, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        if (!TryUnpack(encoded, out EncryptedPayload? payload))
        {
            return SharedKernel.Primitives.Results.Result<string>.Failure(
                SharedKernel.Primitives.Errors.Error.Unexpected(
                    CryptographyErrorCodes.MalformedPayload,
                    "The supplied string is not a valid encrypted payload."));
        }

        SharedKernel.Primitives.Results.Result<byte[]> result = await DecryptAsync(payload, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? SharedKernel.Primitives.Results.Result<string>.Success(Encoding.UTF8.GetString(result.Value))
            : SharedKernel.Primitives.Results.Result<string>.Failure(result.Error);
    }

    private EncryptedPayload EncryptCore(byte[] plaintext, CryptographicKey key)
    {
        byte[] ciphertext = Xor(plaintext, key.Material);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];

        var payload = new EncryptedPayload(key.Id, nonce, ciphertext, tag);
        _encryptedPayloads.Enqueue(payload);
        return payload;
    }

    private static SharedKernel.Primitives.Results.Result<byte[]> DecryptCore(EncryptedPayload payload, CryptographicKey? key)
    {
        if (key is null)
        {
            return SharedKernel.Primitives.Results.Result<byte[]>.Failure(
                SharedKernel.Primitives.Errors.Error.Unexpected(
                    CryptographyErrorCodes.UnknownKeyId,
                    $"No encryption key registered for key id '{payload.KeyId}'."));
        }

        return SharedKernel.Primitives.Results.Result<byte[]>.Success(Xor(payload.Ciphertext, key.Material));
    }

    private static SharedKernel.Primitives.Results.Result<byte[]> SimulatedDecryptFailure() =>
        SharedKernel.Primitives.Results.Result<byte[]>.Failure(
            SharedKernel.Primitives.Errors.Error.Unexpected(
                CryptographyErrorCodes.DecryptionFailed,
                "Decryption failed: the payload may have been tampered with or the wrong key was used."));

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
