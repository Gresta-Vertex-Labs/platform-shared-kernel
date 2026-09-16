using System.Collections.Concurrent;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// A test double for <see cref="IAsymmetricSignatureService"/> that produces real signatures with a
/// <see cref="FakeSigningKeyProvider"/> and records every signing call.
/// </summary>
public sealed class FakeAsymmetricSignatureService : IAsymmetricSignatureService
{
    private readonly AsymmetricSignatureService _inner;
    private readonly ConcurrentQueue<(byte[] Data, string KeyId)> _signed = new();

    /// <summary>Creates the service.</summary>
    /// <param name="keyProvider">The keys to use; a new <see cref="FakeSigningKeyProvider"/> when omitted.</param>
    public FakeAsymmetricSignatureService(FakeSigningKeyProvider? keyProvider = null)
    {
        KeyProvider = keyProvider ?? new FakeSigningKeyProvider();
        _inner = new AsymmetricSignatureService(KeyProvider);
    }

    /// <summary>The key provider, for adding keys with a chosen algorithm.</summary>
    public FakeSigningKeyProvider KeyProvider { get; }

    /// <summary>
    /// Every data buffer successfully signed with <see cref="SignAsync(ReadOnlyMemory{byte}, string, CancellationToken)"/>,
    /// with its key id. Calls that throw are not recorded.
    /// </summary>
    public IReadOnlyList<(byte[] Data, string KeyId)> SignedPayloads => [.. _signed];

    /// <inheritdoc />
    public async ValueTask<byte[]> SignAsync(ReadOnlyMemory<byte> data, string keyId, CancellationToken cancellationToken = default)
    {
        byte[] signature = await _inner.SignAsync(data, keyId, cancellationToken).ConfigureAwait(false);
        _signed.Enqueue((data.ToArray(), keyId));
        return signature;
    }

    /// <inheritdoc />
    public ValueTask<byte[]> SignAsync(Stream data, string keyId, CancellationToken cancellationToken = default) =>
        _inner.SignAsync(data, keyId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> VerifyAsync(
        ReadOnlyMemory<byte> data,
        ReadOnlyMemory<byte> signature,
        string keyId,
        CancellationToken cancellationToken = default) =>
        _inner.VerifyAsync(data, signature, keyId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> VerifyAsync(
        Stream data,
        ReadOnlyMemory<byte> signature,
        string keyId,
        CancellationToken cancellationToken = default) =>
        _inner.VerifyAsync(data, signature, keyId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<SignatureAlgorithm> GetAlgorithmAsync(string keyId, CancellationToken cancellationToken = default) =>
        _inner.GetAlgorithmAsync(keyId, cancellationToken);
}
