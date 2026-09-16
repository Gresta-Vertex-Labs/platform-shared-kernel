using System.Collections.Concurrent;
using System.Security.Cryptography;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Tests.TestDoubles;

/// <summary>
/// Wraps random data keys with AES-GCM under an in-memory master key. <c>master-1</c> is current; <c>master-2</c> is an
/// alias for the same material, so a payload can name either id and still unwrap to the same data key.
/// </summary>
internal sealed class FakeEnvelopeEncryptionProvider : IEnvelopeEncryptionProvider
{
    public const string MasterKeyId = "master-1";
    public const string AliasMasterKeyId = "master-2";

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly Dictionary<string, byte[]> _masterKeys;

    public FakeEnvelopeEncryptionProvider()
    {
        byte[] material = RandomNumberGenerator.GetBytes(32);
        _masterKeys = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [MasterKeyId] = material,
            [AliasMasterKeyId] = material,
        };
    }

    public int DataKeyLength { get; set; } = 32;

    public Func<byte[], byte[]>? TransformUnwrappedKey { get; set; }

    public ConcurrentQueue<EnvelopeDataKey> GeneratedKeys { get; } = new();

    public ConcurrentQueue<(byte[] WrappedKey, string MasterKeyId, CancellationToken Token)> UnwrapCalls { get; } = new();

    public ConcurrentQueue<CancellationToken> GenerateTokens { get; } = new();

    public ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken cancellationToken = default)
    {
        GenerateTokens.Enqueue(cancellationToken);

        byte[] dataKey = RandomNumberGenerator.GetBytes(DataKeyLength);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] ciphertext = new byte[dataKey.Length];

        using (var aes = new AesGcm(_masterKeys[MasterKeyId], TagSize))
        {
            aes.Encrypt(nonce, dataKey, ciphertext, tag);
        }

        var key = new EnvelopeDataKey(dataKey, [.. nonce, .. tag, .. ciphertext], MasterKeyId);
        GeneratedKeys.Enqueue(key);
        return new ValueTask<EnvelopeDataKey>(key);
    }

    public ValueTask<Result<byte[]>> UnwrapDataKeyAsync(
        ReadOnlyMemory<byte> wrappedKey,
        string masterKeyId,
        CancellationToken cancellationToken = default)
    {
        UnwrapCalls.Enqueue((wrappedKey.ToArray(), masterKeyId, cancellationToken));

        if (!_masterKeys.TryGetValue(masterKeyId, out byte[]? masterKey))
        {
            return Failure("The master key id is not configured.");
        }

        ReadOnlySpan<byte> wrapped = wrappedKey.Span;
        if (wrapped.Length <= NonceSize + TagSize)
        {
            return Failure("The wrapped key is too short.");
        }

        byte[] dataKey = new byte[wrapped.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(masterKey, TagSize);
            aes.Decrypt(wrapped[..NonceSize], wrapped[(NonceSize + TagSize)..], wrapped.Slice(NonceSize, TagSize), dataKey);
        }
        catch (AuthenticationTagMismatchException)
        {
            return Failure("The wrapped key did not authenticate.");
        }

        byte[] result = TransformUnwrappedKey is null ? dataKey : TransformUnwrappedKey(dataKey);
        return new ValueTask<Result<byte[]>>(Result<byte[]>.Success(result));
    }

    private static ValueTask<Result<byte[]>> Failure(string message) =>
        new(Result<byte[]>.Failure(Error.Validation("fake.unwrap_failed", message)));
}
