using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Signing;

/// <summary>The default <see cref="IAsymmetricSignatureService"/>.</summary>
/// <remarks>Thread-safe.</remarks>
public sealed class AsymmetricSignatureService : IAsymmetricSignatureService
{
    private readonly ISigningKeyProvider _keyProvider;

    /// <summary>Creates the service.</summary>
    /// <param name="keyProvider">Resolves signing keys.</param>
    public AsymmetricSignatureService(ISigningKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        _keyProvider = keyProvider;
    }

    /// <inheritdoc />
    public async ValueTask<byte[]> SignAsync(ReadOnlyMemory<byte> data, string keyId, CancellationToken cancellationToken = default)
    {
        SigningKey key = await RequireKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
        byte[] hash = Hash(key.HashAlgorithm, data.Span);
        return await key.SignHashAsync(hash, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]> SignAsync(Stream data, string keyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        SigningKey key = await RequireKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
        byte[] hash = await HashAsync(key.HashAlgorithm, data, cancellationToken).ConfigureAwait(false);
        return await key.SignHashAsync(hash, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> VerifyAsync(
        ReadOnlyMemory<byte> data,
        ReadOnlyMemory<byte> signature,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        SigningKey? key = await _keyProvider.GetSigningKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
        return key is not null
            && await VerifyHashAsync(key, Hash(key.HashAlgorithm, data.Span), signature, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> VerifyAsync(
        Stream data,
        ReadOnlyMemory<byte> signature,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(keyId);

        SigningKey? key = await _keyProvider.GetSigningKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return false;
        }

        byte[] hash = await HashAsync(key.HashAlgorithm, data, cancellationToken).ConfigureAwait(false);
        return await VerifyHashAsync(key, hash, signature, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<SignatureAlgorithm> GetAlgorithmAsync(string keyId, CancellationToken cancellationToken = default)
    {
        SigningKey key = await RequireKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
        return key.Algorithm;
    }

    private async ValueTask<SigningKey> RequireKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        return await _keyProvider.GetSigningKeyAsync(keyId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"No signing key has the id '{keyId}'.");
    }

    private static async ValueTask<bool> VerifyHashAsync(
        SigningKey key,
        byte[] hash,
        ReadOnlyMemory<byte> signature,
        CancellationToken cancellationToken)
    {
        try
        {
            return await key.VerifyHashAsync(hash, signature, cancellationToken).ConfigureAwait(false);
        }
        catch (CryptographicException)
        {
            // A malformed signature encoding is an invalid signature, not an error.
            return false;
        }
    }

    private static byte[] Hash(HashAlgorithmName algorithm, ReadOnlySpan<byte> data) => algorithm.Name switch
    {
        "SHA256" => SHA256.HashData(data),
        "SHA384" => SHA384.HashData(data),
        _ => SHA512.HashData(data),
    };

    private static async ValueTask<byte[]> HashAsync(HashAlgorithmName algorithm, Stream data, CancellationToken cancellationToken) =>
        algorithm.Name switch
        {
            "SHA256" => await SHA256.HashDataAsync(data, cancellationToken).ConfigureAwait(false),
            "SHA384" => await SHA384.HashDataAsync(data, cancellationToken).ConfigureAwait(false),
            _ => await SHA512.HashDataAsync(data, cancellationToken).ConfigureAwait(false),
        };
}
