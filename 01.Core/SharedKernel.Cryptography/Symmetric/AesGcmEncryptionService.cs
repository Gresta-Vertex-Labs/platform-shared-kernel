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
/// <para>
/// The synchronous <see cref="Encrypt(byte[], byte[])"/>/<see cref="Decrypt(EncryptedPayload, byte[])"/>/
/// <see cref="EncryptToString(string, byte[])"/>/<see cref="DecryptToString(string, byte[])"/> members and their
/// asynchronous counterparts share the exact same cryptographic core (<c>EncryptCore</c>/
/// <c>DecryptCore</c>) — the only difference between a sync and an async call is how the
/// <see cref="IEncryptionKeyProvider"/> result is awaited. This guarantees byte-identical
/// ciphertext/plaintext behavior between the two call shapes for the same input.
/// </para>
/// <para>
/// <b>(P-492/WO-081)</b> Whether those four synchronous members are usable at all is decided
/// exactly once, at construction time: <see cref="EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(IEncryptionKeyProvider)"/>
/// is evaluated against the supplied <see cref="IEncryptionKeyProvider"/> and cached for the
/// lifetime of this instance. When it reports <see langword="true"/>, every sync member bridges
/// onto the provider via <c>.GetAwaiter().GetResult()</c>, which is genuinely non-blocking. When
/// it reports <see langword="false"/> — the registered provider is not known to be safe, e.g. a
/// raw KMS/HSM-backed <see cref="IEncryptionKeyProvider"/> — every sync member throws
/// <see cref="NotSupportedException"/> immediately instead of silently blocking a real thread;
/// the caller must use the corresponding <c>*Async</c> member. The async members
/// (<see cref="EncryptAsync(byte[], byte[], CancellationToken)"/>,
/// <see cref="DecryptAsync(EncryptedPayload, byte[], CancellationToken)"/>, and their
/// string-convenience counterparts) are always usable regardless of this check.
/// </para>
/// </remarks>
public sealed class AesGcmEncryptionService : ISymmetricEncryptionService
{
    private const int NonceSize = 12; // 96 bits
    private const int TagSize = 16; // 128 bits

    /// <summary>
    /// The only key length this service accepts (P-513/WO-083) — 32 bytes, i.e. AES-256, exactly
    /// matching <see cref="CryptographicKey.Material"/>'s own documented "32 bytes for AES-256"
    /// contract. <see cref="AesGcm"/>'s constructor otherwise silently accepts any BCL-legal AES
    /// key size (16, 24, or 32 bytes), constructing AES-128-GCM or AES-192-GCM from a
    /// shorter/differently-sized key with no complaint — while every doc, XML comment, and NuGet
    /// package description on this platform promises AES-256. An exact-length equality check
    /// (never a minimum) rejects both a too-short key (the headline AES-128-downgrade risk) and a
    /// too-long one (equally a configuration defect; <see cref="AesGcm"/> would otherwise also
    /// silently accept 24 bytes/AES-192).
    /// </summary>
    private const int RequiredKeySizeBytes = 32;

    private readonly IEncryptionKeyProvider _keyProvider;
    private readonly bool _isKeyProviderGenuinelySynchronous;

    /// <summary>Creates a new <see cref="AesGcmEncryptionService"/>.</summary>
    /// <param name="keyProvider">Resolves the current and historical key material.</param>
    public AesGcmEncryptionService(IEncryptionKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        _keyProvider = keyProvider;

        // Computed once, here, and cached for the lifetime of this instance — never re-evaluated
        // per call. See EncryptionKeyProviderCapabilities.IsGenuinelySynchronous: this is a
        // static provider-identity check, not a per-call cache-warmth test.
        _isKeyProviderGenuinelySynchronous = EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(keyProvider);
    }

    /// <inheritdoc />
    public EncryptedPayload Encrypt(byte[] plaintext, byte[] associatedData)
    {
        ThrowIfNotGenuinelySynchronous(nameof(EncryptAsync));
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(associatedData);

        // GENUINELY NON-BLOCKING: the registered IEncryptionKeyProvider was confirmed genuinely
        // synchronous at construction time (see _isKeyProviderGenuinelySynchronous) — this
        // GetAwaiter().GetResult() observes an already-completed ValueTask, it never schedules or
        // waits on a continuation.
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
    public Result<byte[]> Decrypt(EncryptedPayload payload, byte[] associatedData)
    {
        ThrowIfNotGenuinelySynchronous(nameof(DecryptAsync));
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(associatedData);

        // GENUINELY NON-BLOCKING: the registered IEncryptionKeyProvider was confirmed genuinely
        // synchronous at construction time (see _isKeyProviderGenuinelySynchronous) — this
        // GetAwaiter().GetResult() observes an already-completed ValueTask, it never schedules or
        // waits on a continuation.
        CryptographicKey? key = _keyProvider.GetKeyAsync(payload.KeyId).GetAwaiter().GetResult();
        return DecryptCore(payload, associatedData, key);
    }

    /// <inheritdoc />
    public async ValueTask<Result<byte[]>> DecryptAsync(EncryptedPayload payload, byte[] associatedData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(associatedData);

        CryptographicKey? key = await _keyProvider.GetKeyAsync(payload.KeyId, ct).ConfigureAwait(false);
        return DecryptCore(payload, associatedData, key);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>
    /// KEY-MATERIAL ZEROIZATION (P-524/WO-083): the intermediate UTF-8 plaintext <c>byte[]</c>
    /// produced from <paramref name="plaintext"/> is zeroed via
    /// <see cref="CryptographicOperations.ZeroMemory"/> once it has been encrypted — this package
    /// fully owns that buffer's lifetime and never hands it back to the caller (unlike the
    /// primary <see cref="Encrypt(byte[], byte[])"/> overload's <c>byte[]</c>-based plaintext
    /// parameter, which is caller-owned and never zeroed by this class).
    /// </b>
    /// </remarks>
    public string EncryptToString(string plaintext, byte[] associatedData) =>
        EncryptToString(plaintext, associatedData, captureIntermediatePlaintextForTesting: null);

    /// <summary>
    /// Test-only seam (P-524/WO-083), gated via <c>InternalsVisibleTo</c> to this package's own
    /// <c>.Tests</c> project: identical to <see cref="EncryptToString(string, byte[])"/>, except a
    /// caller-supplied callback is invoked with the intermediate UTF-8 plaintext buffer BEFORE it
    /// is zeroed, letting a test capture the exact same array reference and assert it is
    /// genuinely all-zero bytes once this call returns. Never invoked by any production code
    /// path — the public <see cref="EncryptToString(string, byte[])"/> overload always passes
    /// <see langword="null"/>.
    /// </summary>
    internal string EncryptToString(string plaintext, byte[] associatedData, Action<byte[]>? captureIntermediatePlaintextForTesting)
    {
        ThrowIfNotGenuinelySynchronous(nameof(EncryptToStringAsync));
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(associatedData);

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        captureIntermediatePlaintextForTesting?.Invoke(plaintextBytes);

        try
        {
            EncryptedPayload payload = Encrypt(plaintextBytes, associatedData);
            return Pack(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>
    /// KEY-MATERIAL ZEROIZATION (P-524/WO-083): the intermediate UTF-8 plaintext <c>byte[]</c>
    /// produced from <paramref name="plaintext"/> is zeroed via
    /// <see cref="CryptographicOperations.ZeroMemory"/> once it has been encrypted — this package
    /// fully owns that buffer's lifetime and never hands it back to the caller.
    /// </b>
    /// </remarks>
    public async ValueTask<string> EncryptToStringAsync(string plaintext, byte[] associatedData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(associatedData);

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            EncryptedPayload payload = await EncryptAsync(plaintextBytes, associatedData, ct).ConfigureAwait(false);
            return Pack(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>
    /// KEY-MATERIAL ZEROIZATION (P-524/WO-083): the decrypted intermediate plaintext
    /// <c>byte[]</c> (this method's own internal call into <see cref="Decrypt(EncryptedPayload, byte[])"/>)
    /// is zeroed via <see cref="CryptographicOperations.ZeroMemory"/> once its contents have been
    /// copied into the returned <see cref="string"/> — this call site never exposes that
    /// <c>byte[]</c> to its own caller, so zeroing it here is safe and does not affect the
    /// primary <see cref="Decrypt(EncryptedPayload, byte[])"/> overload's own contract (its
    /// directly-returned <c>byte[]</c> is never zeroed).
    /// </b>
    /// </remarks>
    public Result<string> DecryptToString(string encoded, byte[] associatedData)
    {
        ThrowIfNotGenuinelySynchronous(nameof(DecryptToStringAsync));
        ArgumentNullException.ThrowIfNull(encoded);
        ArgumentNullException.ThrowIfNull(associatedData);

        if (!TryUnpack(encoded, out EncryptedPayload? payload))
        {
            return Error.Unexpected(
                CryptographyErrorCodes.MalformedPayload,
                "The supplied string is not a valid encrypted payload.");
        }

        Result<byte[]> result = Decrypt(payload, associatedData);
        if (!result.IsSuccess)
        {
            return result.Error;
        }

        byte[] plaintextBytes = result.Value;
        try
        {
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>
    /// KEY-MATERIAL ZEROIZATION (P-524/WO-083): the decrypted intermediate plaintext
    /// <c>byte[]</c> is zeroed once its contents have been copied into the returned
    /// <see cref="string"/> — see <see cref="DecryptToString(string, byte[])"/>'s remarks for the
    /// full rationale.
    /// </b>
    /// </remarks>
    public async ValueTask<Result<string>> DecryptToStringAsync(string encoded, byte[] associatedData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        ArgumentNullException.ThrowIfNull(associatedData);

        if (!TryUnpack(encoded, out EncryptedPayload? payload))
        {
            return Error.Unexpected(
                CryptographyErrorCodes.MalformedPayload,
                "The supplied string is not a valid encrypted payload.");
        }

        Result<byte[]> result = await DecryptAsync(payload, associatedData, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result.Error;
        }

        byte[] plaintextBytes = result.Value;
        try
        {
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    /// <summary>
    /// Guards a retained synchronous member: throws when the registered
    /// <see cref="IEncryptionKeyProvider"/> was not confirmed genuinely synchronous at
    /// construction time (<see cref="EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(IEncryptionKeyProvider)"/>),
    /// instead of silently bridging onto it via <c>.GetAwaiter().GetResult()</c>.
    /// </summary>
    /// <param name="asyncMemberName">
    /// The name of this member's <c>*Async</c> counterpart, surfaced in the exception message.
    /// </param>
    private void ThrowIfNotGenuinelySynchronous(string asyncMemberName)
    {
        if (!_isKeyProviderGenuinelySynchronous)
        {
            throw new NotSupportedException(
                $"The registered {nameof(IEncryptionKeyProvider)} does not implement " +
                $"{nameof(ISynchronousEncryptionKeyProvider)}, so it cannot be trusted never to " +
                "block the calling thread on a network/IPC round trip. Call " +
                $"{asyncMemberName} instead.");
        }
    }

    /// <summary>
    /// The pure, synchronous AES-256-GCM encryption core shared by both the sync and async
    /// public members — performs no I/O and no key resolution of its own.
    /// </summary>
    private static EncryptedPayload EncryptCore(byte[] plaintext, byte[] associatedData, CryptographicKey key)
    {
        EnsureKeySize(key);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using var aesGcm = new AesGcm(key.Material, TagSize);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return new EncryptedPayload(key.Id, nonce, ciphertext, tag);
    }

    /// <summary>
    /// The pure, synchronous AES-256-GCM decryption core shared by both the sync and async
    /// public members — performs no I/O and no key resolution of its own.
    /// </summary>
    private static Result<byte[]> DecryptCore(EncryptedPayload payload, byte[] associatedData, CryptographicKey? key)
    {
        if (key is null)
        {
            return Error.Unexpected(
                CryptographyErrorCodes.UnknownKeyId,
                $"No encryption key registered for key id '{payload.KeyId}'.");
        }

        EnsureKeySize(key);

        byte[] plaintext = new byte[payload.Ciphertext.Length];

        try
        {
            using var aesGcm = new AesGcm(key.Material, TagSize);
            aesGcm.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, plaintext, associatedData);
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
    /// Rejects any <paramref name="key"/> whose <see cref="CryptographicKey.Material"/> is not
    /// exactly <see cref="RequiredKeySizeBytes"/> bytes long — called at the very start of both
    /// <see cref="EncryptCore"/> and <see cref="DecryptCore"/>, before any <see cref="AesGcm"/>
    /// instance is ever constructed (P-513/WO-083).
    /// </summary>
    /// <remarks>
    /// <b>
    /// THROWS RATHER THAN RETURNING A <see cref="Result{T}"/> FAILURE: a wrong-size key means the
    /// registered <see cref="IEncryptionKeyProvider"/>/its backing key material is misconfigured —
    /// an infrastructure/provisioning defect the caller did not cause and could not have avoided
    /// (it does not control <see cref="CryptographicKey.Material"/>; that comes from
    /// <see cref="IEncryptionKeyProvider"/>), not a runtime/tampered-input condition — which is
    /// what this class's <see cref="Result{T}"/> failures are reserved for (tamper, wrong key,
    /// unknown key id, mismatched associated data). Mirrors
    /// <c>AzureKeyVaultEncryptionKeyProvider.ResolveWrapAlgorithm</c>'s existing precedent of
    /// throwing <see cref="NotSupportedException"/> for an unsupported/misconfigured key type
    /// rather than returning a soft failure.
    /// </b>
    /// </remarks>
    /// <exception cref="CryptographicException">
    /// <paramref name="key"/>'s <see cref="CryptographicKey.Material"/> is not exactly
    /// <see cref="RequiredKeySizeBytes"/> bytes.
    /// </exception>
    private static void EnsureKeySize(CryptographicKey key)
    {
        if (key.Material.Length != RequiredKeySizeBytes)
        {
            throw new CryptographicException(
                $"Invalid AES key length for key id '{key.Id}': expected exactly " +
                $"{RequiredKeySizeBytes} bytes (AES-256), but the resolved key material is " +
                $"{key.Material.Length} bytes. Every key this service is given must be a genuine " +
                "AES-256 key — a shorter or longer key would silently downgrade to AES-128/AES-192 " +
                "or be rejected by the BCL for an unsupported size, neither of which this platform " +
                "permits.");
        }
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
