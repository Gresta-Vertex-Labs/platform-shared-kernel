using System.Collections.Concurrent;
using SharedKernel.Cryptography;
using SharedKernel.Cryptography.Symmetric;
using SkError = SharedKernel.Primitives.Errors.Error;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// A test double for <see cref="ISymmetricEncryptionService"/> and <see cref="ISynchronousSymmetricEncryptionService"/>
/// that performs real AES-256-GCM, records every encryption, and can be told to fail decryption.
/// </summary>
/// <remarks>
/// Payloads are genuine: they round-trip through the production services and parsers, and associated data is
/// enforced exactly as in production.
/// </remarks>
public sealed class FakeSymmetricEncryptionService : ISymmetricEncryptionService, ISynchronousSymmetricEncryptionService
{
    private readonly AesGcmEncryptionService _async;
    private readonly SynchronousAesGcmEncryptionService _sync;
    private readonly ConcurrentQueue<(EncryptedPayload Payload, byte[] AssociatedData)> _encrypted = new();

    /// <summary>Creates the service.</summary>
    /// <param name="keyProvider">The keys to use; a new <see cref="FakeEncryptionKeyProvider"/> when omitted.</param>
    public FakeSymmetricEncryptionService(FakeEncryptionKeyProvider? keyProvider = null)
    {
        KeyProvider = keyProvider ?? new FakeEncryptionKeyProvider();
        _async = new AesGcmEncryptionService(KeyProvider);
        _sync = new SynchronousAesGcmEncryptionService(KeyProvider);
    }

    /// <summary>The key provider, for rotating or removing keys during a test.</summary>
    public FakeEncryptionKeyProvider KeyProvider { get; }

    /// <summary>When <see langword="true"/>, every decryption returns <see cref="CryptographyErrorCodes.DecryptionFailed"/>.</summary>
    public bool SimulateDecryptFailure { get; set; }

    /// <summary>Every payload encrypted so far, with the associated data it was bound to.</summary>
    public IReadOnlyList<(EncryptedPayload Payload, byte[] AssociatedData)> EncryptedPayloads => [.. _encrypted];

    /// <inheritdoc />
    public async ValueTask<EncryptedPayload> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default) =>
        Record(await _async.EncryptAsync(plaintext, associatedData, cancellationToken).ConfigureAwait(false), associatedData.Span);

    /// <inheritdoc />
    public ValueTask<SharedKernel.Primitives.Results.Result<byte[]>> DecryptAsync(
        EncryptedPayload payload,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default) =>
        SimulateDecryptFailure ? new(Failure()) : _async.DecryptAsync(payload, associatedData, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<string> EncryptToStringAsync(
        string plaintext,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default)
    {
        string encoded = await _async.EncryptToStringAsync(plaintext, associatedData, cancellationToken).ConfigureAwait(false);
        RecordEncoded(encoded, associatedData.Span);
        return encoded;
    }

    /// <inheritdoc />
    public ValueTask<SharedKernel.Primitives.Results.Result<string>> DecryptToStringAsync(
        string encoded,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default) =>
        SimulateDecryptFailure ? new(Failure()) : _async.DecryptToStringAsync(encoded, associatedData, cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> IsEncryptedWithCurrentKeyAsync(EncryptedPayload payload, CancellationToken cancellationToken = default) =>
        _async.IsEncryptedWithCurrentKeyAsync(payload, cancellationToken);

    /// <inheritdoc />
    public ValueTask<SharedKernel.Primitives.Results.Result<EncryptedPayload>> ReEncryptAsync(
        EncryptedPayload payload,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default) =>
        SimulateDecryptFailure ? new(Failure()) : _async.ReEncryptAsync(payload, associatedData, cancellationToken);

    /// <inheritdoc />
    public EncryptedPayload Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData) =>
        Record(_sync.Encrypt(plaintext, associatedData), associatedData);

    /// <inheritdoc />
    public SharedKernel.Primitives.Results.Result<byte[]> Decrypt(EncryptedPayload payload, ReadOnlySpan<byte> associatedData) =>
        SimulateDecryptFailure ? Failure() : _sync.Decrypt(payload, associatedData);

    /// <inheritdoc />
    public string EncryptToString(string plaintext, ReadOnlySpan<byte> associatedData)
    {
        string encoded = _sync.EncryptToString(plaintext, associatedData);
        RecordEncoded(encoded, associatedData);
        return encoded;
    }

    /// <inheritdoc />
    public SharedKernel.Primitives.Results.Result<string> DecryptToString(string encoded, ReadOnlySpan<byte> associatedData) =>
        SimulateDecryptFailure ? Failure() : _sync.DecryptToString(encoded, associatedData);

    /// <inheritdoc />
    public bool IsEncryptedWithCurrentKey(EncryptedPayload payload) => _sync.IsEncryptedWithCurrentKey(payload);

    /// <inheritdoc />
    public SharedKernel.Primitives.Results.Result<EncryptedPayload> ReEncrypt(EncryptedPayload payload, ReadOnlySpan<byte> associatedData) =>
        SimulateDecryptFailure ? Failure() : _sync.ReEncrypt(payload, associatedData);

    private EncryptedPayload Record(EncryptedPayload payload, ReadOnlySpan<byte> associatedData)
    {
        _encrypted.Enqueue((payload, associatedData.ToArray()));
        return payload;
    }

    private void RecordEncoded(string encoded, ReadOnlySpan<byte> associatedData)
    {
        if (EncryptedPayload.TryParse(encoded, out EncryptedPayload? payload))
        {
            Record(payload, associatedData);
        }
    }

    private static SkError Failure() => SkError.Validation(
        CryptographyErrorCodes.DecryptionFailed,
        "Decryption failed (simulated by FakeSymmetricEncryptionService).");
}
