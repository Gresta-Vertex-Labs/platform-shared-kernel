using System.Security.Cryptography;
using SharedKernel.Cryptography;
using SharedKernel.Cryptography.Envelope;
using SkError = SharedKernel.Primitives.Errors.Error;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// A test double for <see cref="IEnvelopeEncryptionProvider"/> that wraps data keys with AES-256-GCM under an
/// in-memory master key.
/// </summary>
/// <remarks>
/// Unwrapping checks the master key id and authenticates the wrapped key, so tampering fails as it would in a real
/// key service.
/// </remarks>
public sealed class FakeEnvelopeEncryptionProvider : IEnvelopeEncryptionProvider
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _masterKey = RandomNumberGenerator.GetBytes(32);
    private int _generateCalls;
    private int _unwrapCalls;

    /// <summary>Creates the provider.</summary>
    /// <param name="masterKeyId">The master key id written into every data key.</param>
    public FakeEnvelopeEncryptionProvider(string masterKeyId = "fake-master/v1")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masterKeyId);
        MasterKeyId = masterKeyId;
    }

    /// <summary>The master key id.</summary>
    public string MasterKeyId { get; }

    /// <summary>When <see langword="true"/>, every unwrap fails.</summary>
    public bool SimulateUnwrapFailure { get; set; }

    /// <summary>How many data keys have been generated.</summary>
    public int GenerateCallCount => _generateCalls;

    /// <summary>How many unwraps have been requested.</summary>
    public int UnwrapCallCount => _unwrapCalls;

    /// <inheritdoc />
    public ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _generateCalls);

        byte[] dataKey = RandomNumberGenerator.GetBytes(32);
        byte[] wrapped = new byte[NonceSize + TagSize + dataKey.Length];
        Span<byte> span = wrapped;
        RandomNumberGenerator.Fill(span[..NonceSize]);

        using (var aes = new AesGcm(_masterKey, TagSize))
        {
            aes.Encrypt(span[..NonceSize], dataKey, span[(NonceSize + TagSize)..], span.Slice(NonceSize, TagSize));
        }

        try
        {
            return new(new EnvelopeDataKey(dataKey, wrapped, MasterKeyId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <inheritdoc />
    public ValueTask<SharedKernel.Primitives.Results.Result<byte[]>> UnwrapDataKeyAsync(
        ReadOnlyMemory<byte> wrappedKey,
        string masterKeyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(masterKeyId);
        Interlocked.Increment(ref _unwrapCalls);

        ReadOnlySpan<byte> span = wrappedKey.Span;
        if (SimulateUnwrapFailure || !string.Equals(masterKeyId, MasterKeyId, StringComparison.Ordinal) || span.Length <= NonceSize + TagSize)
        {
            return new(Failure());
        }

        byte[] dataKey = new byte[span.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(_masterKey, TagSize);
            aes.Decrypt(span[..NonceSize], span[(NonceSize + TagSize)..], span.Slice(NonceSize, TagSize), dataKey);
            return new(dataKey);
        }
        catch (AuthenticationTagMismatchException)
        {
            return new(Failure());
        }
    }

    private static SkError Failure() => SkError.Validation(
        CryptographyErrorCodes.DataKeyUnwrapFailed,
        "The data key could not be unwrapped (FakeEnvelopeEncryptionProvider).");
}
