using System.Security.Cryptography;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Tests.Symmetric;

/// <summary>
/// A minimal in-memory <see cref="IEnvelopeEncryptionProvider"/> test double proving the
/// contract's shape (wrap/unwrap round-trip, unknown-master-key and tamper failure modes) without
/// a real KMS. Wraps a generated data key by AES-256-GCM-encrypting it under a process-lifetime
/// "master key" — a stand-in for a vendor KMS's `WrapKey`/`UnwrapKey` operation.
/// </summary>
internal sealed class InMemoryEnvelopeEncryptionProvider : IEnvelopeEncryptionProvider
{
    private const string MasterKeyId = "test-master-v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _masterKey = RandomNumberGenerator.GetBytes(32);
    private readonly int _dataKeyLength;

    public InMemoryEnvelopeEncryptionProvider(int dataKeyLength = 32) => _dataKeyLength = dataKeyLength;

    public ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken ct = default)
    {
        byte[] plaintextKey = RandomNumberGenerator.GetBytes(_dataKeyLength);
        byte[] wrappedKey = Wrap(plaintextKey);
        return new(new EnvelopeDataKey(plaintextKey, wrappedKey, MasterKeyId));
    }

    public ValueTask<Result<byte[]>> UnwrapDataKeyAsync(byte[] wrappedDataKey, string masterKeyId, CancellationToken ct = default)
    {
        if (masterKeyId != MasterKeyId)
        {
            return new(Result<byte[]>.Failure(
                Error.Unexpected("test.envelope.unknown_master_key", $"Unknown master key id '{masterKeyId}'.")));
        }

        try
        {
            return new(Result<byte[]>.Success(Unwrap(wrappedDataKey)));
        }
        catch (CryptographicException)
        {
            return new(Result<byte[]>.Failure(
                Error.Unexpected("test.envelope.unwrap_failed", "Failed to unwrap the data key — it may have been tampered with.")));
        }
    }

    private byte[] Wrap(byte[] plaintext)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using var aesGcm = new AesGcm(_masterKey, TagSize);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        return [.. nonce, .. tag, .. ciphertext];
    }

    private byte[] Unwrap(byte[] wrapped)
    {
        ReadOnlySpan<byte> span = wrapped;
        byte[] nonce = span[..NonceSize].ToArray();
        byte[] tag = span[NonceSize..(NonceSize + TagSize)].ToArray();
        byte[] ciphertext = span[(NonceSize + TagSize)..].ToArray();
        byte[] plaintext = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(_masterKey, TagSize);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }
}
