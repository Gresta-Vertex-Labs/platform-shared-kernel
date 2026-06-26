using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Signing;

/// <summary>
/// Signs and verifies data using ECDSA on the P-256 curve with SHA-256.
/// </summary>
public sealed class EcdsaSignatureService : IAsymmetricSignatureService
{
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    private readonly IAsymmetricKeyProvider _keyProvider;

    /// <summary>Creates a new <see cref="EcdsaSignatureService"/>.</summary>
    /// <param name="keyProvider">Resolves ECDSA key pairs by key id.</param>
    public EcdsaSignatureService(IAsymmetricKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        _keyProvider = keyProvider;
    }

    /// <inheritdoc />
    public byte[] Sign(byte[] data, string keyId)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(keyId);

        using ECDsa ecdsa = _keyProvider.GetEcdsaKey(keyId);
        return ecdsa.SignData(data, HashAlgorithm);
    }

    /// <inheritdoc />
    public bool Verify(byte[] data, byte[] signature, string keyId)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(keyId);

        using ECDsa ecdsa = _keyProvider.GetEcdsaKey(keyId);
        return ecdsa.VerifyData(data, signature, HashAlgorithm);
    }
}
