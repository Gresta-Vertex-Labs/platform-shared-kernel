using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Signing;

/// <summary>
/// Signs and verifies data using RSA (2048-bit minimum) with PSS padding and SHA-256.
/// </summary>
public sealed class RsaSignatureService : IAsymmetricSignatureService
{
    private const int MinimumKeySizeBits = 2048;
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;
    private static readonly RSASignaturePadding Padding = RSASignaturePadding.Pss;

    private readonly IAsymmetricKeyProvider _keyProvider;

    /// <summary>Creates a new <see cref="RsaSignatureService"/>.</summary>
    /// <param name="keyProvider">Resolves RSA key pairs by key id.</param>
    public RsaSignatureService(IAsymmetricKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        _keyProvider = keyProvider;
    }

    /// <inheritdoc />
    public byte[] Sign(byte[] data, string keyId)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(keyId);

        using RSA rsa = _keyProvider.GetRsaKey(keyId);
        EnsureMinimumKeySize(rsa);
        return rsa.SignData(data, HashAlgorithm, Padding);
    }

    /// <inheritdoc />
    public bool Verify(byte[] data, byte[] signature, string keyId)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(keyId);

        using RSA rsa = _keyProvider.GetRsaKey(keyId);
        return rsa.VerifyData(data, signature, HashAlgorithm, Padding);
    }

    private static void EnsureMinimumKeySize(RSA rsa)
    {
        if (rsa.KeySize < MinimumKeySizeBits)
        {
            throw new CryptographicException(
                $"RSA key size {rsa.KeySize} bits is below the minimum required {MinimumKeySizeBits} bits.");
        }
    }
}
