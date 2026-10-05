using System.Security.Cryptography;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Cryptography.Tests.Signing;

[Collection(SigningKeyCollection.Name)]
public sealed class SigningKeyTests(SigningKeyMaterial keys)
{
    [Theory]
    [MemberData(nameof(SigningKeyMaterial.RsaAlgorithms), MemberType = typeof(SigningKeyMaterial))]
    public void FromRsa_RsaAlgorithm_CreatesKeyBoundToAlgorithm(SignatureAlgorithm algorithm)
    {
        using SigningKey key = SigningKey.FromRsa("rsa-1", keys.Rsa(), algorithm, ownsKey: false);

        Assert.Equal("rsa-1", key.KeyId);
        Assert.Equal(algorithm, key.Algorithm);
        Assert.Equal(SigningKey.GetHashAlgorithm(algorithm), key.HashAlgorithm);
    }

    [Theory]
    [MemberData(nameof(SigningKeyMaterial.EcdsaAlgorithms), MemberType = typeof(SigningKeyMaterial))]
    public void FromRsa_EcdsaAlgorithm_Throws(SignatureAlgorithm algorithm)
    {
        Assert.Throws<ArgumentException>(() => SigningKey.FromRsa("rsa-1", keys.Rsa(), algorithm, ownsKey: false));
    }

    [Fact]
    public void FromRsa_UndefinedAlgorithm_Throws()
    {
        Assert.Throws<ArgumentException>(() => SigningKey.FromRsa("rsa-1", keys.Rsa(), (SignatureAlgorithm)0, ownsKey: false));
        Assert.Throws<ArgumentException>(() => SigningKey.FromRsa("rsa-1", keys.Rsa(), (SignatureAlgorithm)42, ownsKey: false));
    }

    [Fact]
    public void FromRsa_KeySmallerThan2048Bits_Throws()
    {
        using var rsa = RSA.Create(1024);

        Assert.Throws<ArgumentException>(() => SigningKey.FromRsa("rsa-1", rsa, SignatureAlgorithm.PS256));
    }

    [Fact]
    public void FromRsa_InvalidKeyIdOrNullKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => SigningKey.FromRsa(" ", keys.Rsa(), SignatureAlgorithm.PS256, ownsKey: false));
        Assert.Throws<ArgumentNullException>(() => SigningKey.FromRsa("k", null!, SignatureAlgorithm.PS256));
    }

    [Theory]
    [InlineData(SignatureAlgorithm.ES256)]
    [InlineData(SignatureAlgorithm.ES384)]
    [InlineData(SignatureAlgorithm.ES512)]
    public void FromECDsa_NistCurve_SelectsAlgorithmFromCurve(SignatureAlgorithm expected)
    {
        using SigningKey key = SigningKey.FromECDsa("ec-1", keys.Ecdsa(expected), ownsKey: false);

        Assert.Equal(expected, key.Algorithm);
        Assert.Equal("ec-1", key.KeyId);
    }

    [Fact]
    public void FromECDsa_NonNistCurve_Throws()
    {
        ECDsa brainpool;
        try
        {
            brainpool = ECDsa.Create(ECCurve.NamedCurves.brainpoolP256r1);
        }
        catch (PlatformNotSupportedException)
        {
            return;
        }

        using (brainpool)
        {
            Assert.Throws<ArgumentException>(() => SigningKey.FromECDsa("ec-1", brainpool, ownsKey: false));
        }
    }

    [Fact]
    public void FromECDsa_NullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SigningKey.FromECDsa("ec-1", null!));
    }

    [Theory]
    [InlineData(SignatureAlgorithm.PS256, "SHA256")]
    [InlineData(SignatureAlgorithm.RS256, "SHA256")]
    [InlineData(SignatureAlgorithm.ES256, "SHA256")]
    [InlineData(SignatureAlgorithm.PS384, "SHA384")]
    [InlineData(SignatureAlgorithm.RS384, "SHA384")]
    [InlineData(SignatureAlgorithm.ES384, "SHA384")]
    [InlineData(SignatureAlgorithm.PS512, "SHA512")]
    [InlineData(SignatureAlgorithm.RS512, "SHA512")]
    [InlineData(SignatureAlgorithm.ES512, "SHA512")]
    public void GetHashAlgorithm_ReturnsDigestForAlgorithm(SignatureAlgorithm algorithm, string expected)
    {
        Assert.Equal(new HashAlgorithmName(expected), SigningKey.GetHashAlgorithm(algorithm));
    }

    [Fact]
    public void GetHashAlgorithm_UndefinedAlgorithm_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SigningKey.GetHashAlgorithm((SignatureAlgorithm)99));
    }

    [Fact]
    public void GetEcdsaAlgorithm_NamedNistCurves_ReturnAlgorithm()
    {
        Assert.Equal(SignatureAlgorithm.ES256, SigningKey.GetEcdsaAlgorithm(ECCurve.NamedCurves.nistP256));
        Assert.Equal(SignatureAlgorithm.ES384, SigningKey.GetEcdsaAlgorithm(ECCurve.NamedCurves.nistP384));
        Assert.Equal(SignatureAlgorithm.ES512, SigningKey.GetEcdsaAlgorithm(ECCurve.NamedCurves.nistP521));
    }

    [Theory]
    [InlineData("1.2.840.10045.3.1.7", SignatureAlgorithm.ES256)]
    [InlineData("1.3.132.0.34", SignatureAlgorithm.ES384)]
    [InlineData("1.3.132.0.35", SignatureAlgorithm.ES512)]
    public void GetEcdsaAlgorithm_CurveFromOidValue_ReturnsAlgorithm(string oid, SignatureAlgorithm expected)
    {
        Assert.Equal(expected, SigningKey.GetEcdsaAlgorithm(ECCurve.CreateFromValue(oid)));
    }

    [Theory]
    [InlineData("ECDSA_P256", SignatureAlgorithm.ES256)]
    [InlineData("secp384r1", SignatureAlgorithm.ES384)]
    [InlineData("nistP521", SignatureAlgorithm.ES512)]
    public void GetEcdsaAlgorithm_CurveFromFriendlyName_ReturnsAlgorithm(string friendlyName, SignatureAlgorithm expected)
    {
        Assert.Equal(expected, SigningKey.GetEcdsaAlgorithm(ECCurve.CreateFromFriendlyName(friendlyName)));
    }

    [Fact]
    public void GetEcdsaAlgorithm_OtherCurves_ReturnNull()
    {
        Assert.Null(SigningKey.GetEcdsaAlgorithm(ECCurve.NamedCurves.brainpoolP256r1));
        Assert.Null(SigningKey.GetEcdsaAlgorithm(ECCurve.CreateFromValue("1.3.132.0.10")));
        Assert.Null(SigningKey.GetEcdsaAlgorithm(keys.Ecdsa(SignatureAlgorithm.ES256).ExportExplicitParameters(false).Curve));
        Assert.Null(SigningKey.GetEcdsaAlgorithm(default));
    }

    [Theory]
    [MemberData(nameof(SigningKeyMaterial.AllAlgorithms), MemberType = typeof(SigningKeyMaterial))]
    public async Task SignHashAsync_WrongDigestLength_Throws(SignatureAlgorithm algorithm)
    {
        using SigningKey key = keys.CreateKey("k", algorithm);
        int length = SigningKey.GetHashAlgorithm(algorithm) == HashAlgorithmName.SHA256 ? 48 : 32;

        await Assert.ThrowsAsync<ArgumentException>(async () => await key.SignHashAsync(new byte[length]));
        await Assert.ThrowsAsync<ArgumentException>(async () => await key.VerifyHashAsync(new byte[length], new byte[64]));
    }

    [Theory]
    [MemberData(nameof(SigningKeyMaterial.AllAlgorithms), MemberType = typeof(SigningKeyMaterial))]
    public async Task SignHashAsync_VerifyHashAsync_RoundTripOverDigest(SignatureAlgorithm algorithm)
    {
        using SigningKey key = keys.CreateKey("k", algorithm);
        byte[] digest = SigningKey.GetHashAlgorithm(algorithm).Name switch
        {
            "SHA256" => SHA256.HashData("data"u8),
            "SHA384" => SHA384.HashData("data"u8),
            _ => SHA512.HashData("data"u8),
        };

        byte[] signature = await key.SignHashAsync(digest);

        Assert.True(await key.VerifyHashAsync(digest, signature));
    }

    [Fact]
    public async Task Dispose_OwnsKeyTrue_DisposesUnderlyingRsaKey()
    {
        var rsa = RSA.Create(2048);
        SigningKey key = SigningKey.FromRsa("k", rsa, SignatureAlgorithm.PS256);

        key.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await key.SignHashAsync(new byte[32]));
        Assert.Throws<ObjectDisposedException>(() => rsa.SignHash(new byte[32], HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    }

    [Fact]
    public async Task Dispose_OwnsKeyTrue_DisposesUnderlyingEcdsaKey()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SigningKey key = SigningKey.FromECDsa("k", ecdsa);

        key.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await key.SignHashAsync(new byte[32]));
    }

    [Fact]
    public async Task Dispose_OwnsKeyFalse_LeavesUnderlyingKeysUsable()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SigningKey rsaKey = SigningKey.FromRsa("rsa", keys.Rsa(), SignatureAlgorithm.PS256, ownsKey: false);
        SigningKey ecdsaKey = SigningKey.FromECDsa("ec", ecdsa, ownsKey: false);

        rsaKey.Dispose();
        ecdsaKey.Dispose();

        Assert.NotEmpty(keys.Rsa().SignHash(new byte[32], HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        Assert.NotEmpty(ecdsa.SignHash(new byte[32]));
        using SigningKey reused = SigningKey.FromECDsa("ec", ecdsa, ownsKey: false);
        Assert.NotEmpty(await reused.SignHashAsync(new byte[32]));
    }
}
