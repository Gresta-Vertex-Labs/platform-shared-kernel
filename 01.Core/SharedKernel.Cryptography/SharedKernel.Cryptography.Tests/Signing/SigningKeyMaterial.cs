using System.Security.Cryptography;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Cryptography.Tests.Signing;

/// <summary>Asymmetric keys generated once and shared by the signing tests, which run sequentially in one collection.</summary>
public sealed class SigningKeyMaterial : IDisposable
{
    private readonly RSA _rsaA = RSA.Create(2048);
    private readonly RSA _rsaB = RSA.Create(2048);
    private readonly Dictionary<SignatureAlgorithm, (ECDsa A, ECDsa B)> _ecdsa = new()
    {
        [SignatureAlgorithm.ES256] = (ECDsa.Create(ECCurve.NamedCurves.nistP256), ECDsa.Create(ECCurve.NamedCurves.nistP256)),
        [SignatureAlgorithm.ES384] = (ECDsa.Create(ECCurve.NamedCurves.nistP384), ECDsa.Create(ECCurve.NamedCurves.nistP384)),
        [SignatureAlgorithm.ES512] = (ECDsa.Create(ECCurve.NamedCurves.nistP521), ECDsa.Create(ECCurve.NamedCurves.nistP521)),
    };

    public static TheoryData<SignatureAlgorithm> AllAlgorithms { get; } = new(Enum.GetValues<SignatureAlgorithm>());

    public static TheoryData<SignatureAlgorithm> RsaAlgorithms { get; } = new(
        SignatureAlgorithm.PS256,
        SignatureAlgorithm.PS384,
        SignatureAlgorithm.PS512,
        SignatureAlgorithm.RS256,
        SignatureAlgorithm.RS384,
        SignatureAlgorithm.RS512);

    public static TheoryData<SignatureAlgorithm> EcdsaAlgorithms { get; } = new(
        SignatureAlgorithm.ES256,
        SignatureAlgorithm.ES384,
        SignatureAlgorithm.ES512);

    public static bool IsRsa(SignatureAlgorithm algorithm) => algorithm is >= SignatureAlgorithm.PS256 and <= SignatureAlgorithm.RS512;

    public RSA Rsa(bool second = false) => second ? _rsaB : _rsaA;

    public ECDsa Ecdsa(SignatureAlgorithm algorithm, bool second = false) => second ? _ecdsa[algorithm].B : _ecdsa[algorithm].A;

    /// <summary>Creates a signing key over shared material that the key does not own.</summary>
    public SigningKey CreateKey(
        string keyId,
        SignatureAlgorithm algorithm,
        bool second = false,
        DSASignatureFormat format = DSASignatureFormat.IeeeP1363FixedFieldConcatenation) =>
        IsRsa(algorithm)
            ? SigningKey.FromRsa(keyId, Rsa(second), algorithm, ownsKey: false)
            : SigningKey.FromECDsa(keyId, Ecdsa(algorithm, second), format, ownsKey: false);

    public void Dispose()
    {
        _rsaA.Dispose();
        _rsaB.Dispose();
        foreach ((ECDsa a, ECDsa b) in _ecdsa.Values)
        {
            a.Dispose();
            b.Dispose();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SigningKeyCollection : ICollectionFixture<SigningKeyMaterial>
{
    public const string Name = "Signing keys";
}
