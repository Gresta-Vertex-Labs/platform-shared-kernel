using System.Security.Cryptography;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Internal;
using SharedKernel.Cryptography.Signing;
using AzureSignatureAlgorithm = Azure.Security.KeyVault.Keys.Cryptography.SignatureAlgorithm;
using SignatureAlgorithm = SharedKernel.Cryptography.Signing.SignatureAlgorithm;

namespace SharedKernel.Cryptography.KeyVault.Azure;

/// <summary>Signing keys held in Azure Key Vault.</summary>
/// <remarks>
/// <para>
/// Only the key ids in <see cref="AzureKeyVaultSigningOptions.Keys"/> are looked up; any other id returns
/// <see langword="null"/> without a Key Vault call. A key's metadata and public key are read once per
/// <see cref="AzureKeyVaultSigningOptions.RefreshInterval"/>, and checked against the configured algorithm: RSA keys
/// must be at least 2048 bits, EC keys must be on the curve the algorithm names.
/// </para>
/// <para>
/// Signing is an asynchronous Key Vault call over the digest; the private key never leaves Key Vault. Verification
/// runs locally with the public key and makes no call.
/// </para>
/// </remarks>
public sealed class AzureKeyVaultSigningKeyProvider : ISigningKeyProvider
{
    private readonly AzureKeyVaultSigningOptions _options;
    private readonly KeyClient _keyClient;
    private readonly SingleFlightCache<string, SigningKey> _keys;

    /// <summary>Creates the provider.</summary>
    /// <param name="options">The signing settings.</param>
    /// <param name="keyClient">A client for the vault holding the keys.</param>
    /// <param name="timeProvider">The clock used for refresh.</param>
    public AzureKeyVaultSigningKeyProvider(IOptions<AzureKeyVaultSigningOptions> options, KeyClient keyClient, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(keyClient);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = options.Value;
        _keyClient = keyClient;
        _keys = new SingleFlightCache<string, SigningKey>(
            timeProvider,
            _options.RefreshInterval,
            maxEntries: Math.Max(1, _options.Keys?.Count ?? 0),
            comparer: StringComparer.Ordinal);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The Key Vault key does not match its configured algorithm.</exception>
    public async ValueTask<SigningKey?> GetSigningKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        if (_options.Keys is null || !_options.Keys.TryGetValue(keyId, out AzureKeyVaultSigningKeyOptions? settings))
        {
            return null;
        }

        return await _keys.GetOrAddAsync(keyId, token => LoadAsync(keyId, settings, token), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<SigningKey> LoadAsync(string keyId, AzureKeyVaultSigningKeyOptions settings, CancellationToken cancellationToken)
    {
        SignatureAlgorithm algorithm = settings.Algorithm
            ?? throw new InvalidOperationException($"The signing key '{keyId}' has no algorithm configured.");

        KeyVaultKey key = (await _keyClient.GetKeyAsync(settings.KeyName, settings.KeyVersion, cancellationToken).ConfigureAwait(false)).Value;
        CryptographyClient client = _keyClient.GetCryptographyClient(settings.KeyName, key.Properties.Version);

        if (IsRsa(algorithm))
        {
            if (key.KeyType != KeyType.Rsa && key.KeyType != KeyType.RsaHsm)
            {
                throw Mismatch(keyId, algorithm, key.KeyType.ToString());
            }

            RSA publicKey = key.Key.ToRSA(includePrivateParameters: false);
            if (publicKey.KeySize < SigningKey.MinimumRsaKeySize)
            {
                publicKey.Dispose();
                throw new InvalidOperationException(
                    $"The Key Vault key for signing key '{keyId}' is {publicKey.KeySize} bits; at least {SigningKey.MinimumRsaKeySize} are required.");
            }

            return new AzureKeyVaultSigningKey(keyId, algorithm, client, publicKey, ecdsaPublicKey: null);
        }

        if (key.KeyType != KeyType.Ec && key.KeyType != KeyType.EcHsm)
        {
            throw Mismatch(keyId, algorithm, key.KeyType.ToString());
        }

        ECDsa ecdsa = key.Key.ToECDsa(includePrivateParameters: false);
        if (SigningKey.GetEcdsaAlgorithm(ecdsa.ExportParameters(includePrivateParameters: false).Curve) != algorithm)
        {
            ecdsa.Dispose();
            throw Mismatch(keyId, algorithm, $"EC {key.Key.CurveName}");
        }

        return new AzureKeyVaultSigningKey(keyId, algorithm, client, rsaPublicKey: null, ecdsa);
    }

    private static bool IsRsa(SignatureAlgorithm algorithm) => algorithm is >= SignatureAlgorithm.PS256 and <= SignatureAlgorithm.RS512;

    private static InvalidOperationException Mismatch(string keyId, SignatureAlgorithm algorithm, string actual) => new(
        $"The Key Vault key for signing key '{keyId}' is {actual}, which cannot sign with {algorithm}.");

    private sealed class AzureKeyVaultSigningKey(
        string keyId,
        SignatureAlgorithm algorithm,
        CryptographyClient client,
        RSA? rsaPublicKey,
        ECDsa? ecdsaPublicKey)
        : SigningKey(keyId, algorithm)
    {
        public override async ValueTask<byte[]> SignHashAsync(ReadOnlyMemory<byte> hash, CancellationToken cancellationToken = default)
        {
            byte[] digest = EnsureHashLength(hash.Span).ToArray();
            SignResult result = await client.SignAsync(ToAzure(Algorithm), digest, cancellationToken).ConfigureAwait(false);
            return result.Signature;
        }

        public override ValueTask<bool> VerifyHashAsync(
            ReadOnlyMemory<byte> hash,
            ReadOnlyMemory<byte> signature,
            CancellationToken cancellationToken = default)
        {
            ReadOnlySpan<byte> digest = EnsureHashLength(hash.Span);
            bool valid = rsaPublicKey is not null
                ? rsaPublicKey.VerifyHash(
                    digest,
                    signature.Span,
                    HashAlgorithm,
                    Algorithm is >= SignatureAlgorithm.PS256 and <= SignatureAlgorithm.PS512 ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1)
                : ecdsaPublicKey!.VerifyHash(digest, signature.Span, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return new(valid);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                rsaPublicKey?.Dispose();
                ecdsaPublicKey?.Dispose();
            }
        }

        private static AzureSignatureAlgorithm ToAzure(SignatureAlgorithm algorithm) => algorithm switch
        {
            SignatureAlgorithm.PS256 => AzureSignatureAlgorithm.PS256,
            SignatureAlgorithm.PS384 => AzureSignatureAlgorithm.PS384,
            SignatureAlgorithm.PS512 => AzureSignatureAlgorithm.PS512,
            SignatureAlgorithm.RS256 => AzureSignatureAlgorithm.RS256,
            SignatureAlgorithm.RS384 => AzureSignatureAlgorithm.RS384,
            SignatureAlgorithm.RS512 => AzureSignatureAlgorithm.RS512,
            SignatureAlgorithm.ES256 => AzureSignatureAlgorithm.ES256,
            SignatureAlgorithm.ES384 => AzureSignatureAlgorithm.ES384,
            _ => AzureSignatureAlgorithm.ES512,
        };
    }
}
