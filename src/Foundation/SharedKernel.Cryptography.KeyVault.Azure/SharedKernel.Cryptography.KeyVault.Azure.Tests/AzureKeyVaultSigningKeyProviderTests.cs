using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.KeyVault.Azure.Tests.Fakes;
using SharedKernel.Cryptography.Signing;
using Xunit;
using AzureSignatureAlgorithm = Azure.Security.KeyVault.Keys.Cryptography.SignatureAlgorithm;
using SignatureAlgorithm = SharedKernel.Cryptography.Signing.SignatureAlgorithm;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests;

public sealed class AzureKeyVaultSigningKeyProviderTests
{
    private const string KeyId = "webhooks";
    private const string KeyName = "webhook-signing";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);
    private static readonly byte[] Data = Encoding.UTF8.GetBytes("{\"event\":\"order.paid\",\"amount\":4200}");

    private readonly FakeKeyVault _vault = new();
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AzureKeyVaultSigningOptions());

        Assert.Throws<ArgumentNullException>(() => new AzureKeyVaultSigningKeyProvider(null!, _vault.KeyClient, _time));
        Assert.Throws<ArgumentNullException>(() => new AzureKeyVaultSigningKeyProvider(options, null!, _time));
        Assert.Throws<ArgumentNullException>(() => new AzureKeyVaultSigningKeyProvider(options, _vault.KeyClient, null!));
    }

    [Fact]
    public async Task GetSigningKeyAsync_UnknownKeyId_ReturnsNullWithoutVaultCalls()
    {
        _vault.AddRsaKey(KeyName);
        AzureKeyVaultSigningKeyProvider provider = CreateProvider(SignatureAlgorithm.PS256);

        Assert.Null(await provider.GetSigningKeyAsync("unknown"));
        Assert.Null(await provider.GetSigningKeyAsync(KeyName));
        Assert.Null(await provider.GetSigningKeyAsync("WEBHOOKS"));
        Assert.Equal(0, _vault.TotalCalls);
    }

    [Fact]
    public async Task GetSigningKeyAsync_NullKeyId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await CreateProvider(SignatureAlgorithm.PS256).GetSigningKeyAsync(null!));
    }

    [Theory]
    [InlineData(SignatureAlgorithm.PS256)]
    [InlineData(SignatureAlgorithm.PS384)]
    [InlineData(SignatureAlgorithm.PS512)]
    [InlineData(SignatureAlgorithm.RS256)]
    [InlineData(SignatureAlgorithm.RS384)]
    [InlineData(SignatureAlgorithm.RS512)]
    public async Task AsymmetricSignatureService_RsaKey_SignsRemotelyAndVerifiesLocally(SignatureAlgorithm algorithm)
    {
        string version = _vault.AddRsaKey(KeyName);
        var service = new AsymmetricSignatureService(CreateProvider(algorithm));

        byte[] signature = await service.SignAsync(Data, KeyId);
        int signCalls = _vault.SignCalls;
        int totalCalls = _vault.TotalCalls;

        Assert.True(await service.VerifyAsync(Data, signature, KeyId));
        Assert.False(await service.VerifyAsync(Tamper(Data), signature, KeyId));
        Assert.False(await service.VerifyAsync(Data, Tamper(signature), KeyId));
        Assert.Equal(1, signCalls);
        Assert.Equal(totalCalls, _vault.TotalCalls);
        Assert.Equal([ToAzure(algorithm)], _vault.SignAlgorithms);
        Assert.Equal(algorithm, await service.GetAlgorithmAsync(KeyId));

        (HashAlgorithmName hash, RSASignaturePadding padding) = RsaParameters(algorithm);
        using RSA publicKey = RSA.Create(_vault.GetRsa(KeyName, version).ExportParameters(includePrivateParameters: false));
        Assert.True(publicKey.VerifyData(Data, signature, hash, padding));
    }

    [Theory]
    [InlineData(SignatureAlgorithm.ES256, "nistP256")]
    [InlineData(SignatureAlgorithm.ES384, "nistP384")]
    [InlineData(SignatureAlgorithm.ES512, "nistP521")]
    public async Task AsymmetricSignatureService_EcKey_SignsRemotelyAndVerifiesLocally(SignatureAlgorithm algorithm, string curve)
    {
        _vault.AddEcKey(KeyName, ECCurve.CreateFromFriendlyName(curve));
        var service = new AsymmetricSignatureService(CreateProvider(algorithm));

        byte[] signature = await service.SignAsync(Data, KeyId);
        int totalCalls = _vault.TotalCalls;

        Assert.True(await service.VerifyAsync(Data, signature, KeyId));
        Assert.False(await service.VerifyAsync(Tamper(Data), signature, KeyId));
        Assert.False(await service.VerifyAsync(Data, Tamper(signature), KeyId));
        Assert.False(await service.VerifyAsync(Data, signature.AsMemory(1), KeyId));
        Assert.Equal(1, _vault.SignCalls);
        Assert.Equal(totalCalls, _vault.TotalCalls);
        Assert.Equal([ToAzure(algorithm)], _vault.SignAlgorithms);
    }

    [Fact]
    public async Task AsymmetricSignatureService_StreamSignature_VerifiesAgainstMemorySignature()
    {
        _vault.AddRsaKey(KeyName);
        var service = new AsymmetricSignatureService(CreateProvider(SignatureAlgorithm.PS256));

        using var signStream = new MemoryStream(Data);
        byte[] signature = await service.SignAsync(signStream, KeyId);

        Assert.True(await service.VerifyAsync(Data, signature, KeyId));
        using var verifyStream = new MemoryStream(Data);
        Assert.True(await service.VerifyAsync(verifyStream, signature, KeyId));
    }

    [Theory]
    [InlineData("rsa2048", SignatureAlgorithm.ES256)]
    [InlineData("rsa2048", SignatureAlgorithm.ES512)]
    [InlineData("p256", SignatureAlgorithm.ES384)]
    [InlineData("p384", SignatureAlgorithm.ES256)]
    [InlineData("p521", SignatureAlgorithm.ES384)]
    [InlineData("rsa1024", SignatureAlgorithm.PS256)]
    [InlineData("rsa1024", SignatureAlgorithm.RS256)]
    [InlineData("p256", SignatureAlgorithm.PS256)]
    [InlineData("p256", SignatureAlgorithm.RS512)]
    [InlineData("oct", SignatureAlgorithm.PS256)]
    [InlineData("oct", SignatureAlgorithm.ES256)]
    public async Task GetSigningKeyAsync_KeyDoesNotMatchAlgorithm_ThrowsInvalidOperationException(string keyKind, SignatureAlgorithm algorithm)
    {
        switch (keyKind)
        {
            case "rsa2048":
                _vault.AddRsaKey(KeyName);
                break;
            case "rsa1024":
                _vault.AddRsaKey(KeyName, RSA.Create(1024));
                break;
            case "p256":
                _vault.AddEcKey(KeyName, ECCurve.NamedCurves.nistP256);
                break;
            case "p384":
                _vault.AddEcKey(KeyName, ECCurve.NamedCurves.nistP384);
                break;
            case "p521":
                _vault.AddEcKey(KeyName, ECCurve.NamedCurves.nistP521);
                break;
            default:
                _vault.AddOctKey(KeyName);
                break;
        }

        AzureKeyVaultSigningKeyProvider provider = CreateProvider(algorithm);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetSigningKeyAsync(KeyId));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetSigningKeyAsync(KeyId));
        Assert.Equal(2, _vault.GetKeyCalls);
        Assert.Equal(0, _vault.SignCalls);
    }

    [Fact]
    public async Task GetSigningKeyAsync_NoAlgorithmConfigured_ThrowsInvalidOperationExceptionWithoutVaultCalls()
    {
        _vault.AddRsaKey(KeyName);
        AzureKeyVaultSigningKeyProvider provider = CreateProvider(new AzureKeyVaultSigningKeyOptions { KeyName = KeyName });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetSigningKeyAsync(KeyId));
        Assert.Equal(0, _vault.TotalCalls);
    }

    [Fact]
    public async Task GetSigningKeyAsync_KeyVersionPinned_ReadsAndSignsWithThatVersion()
    {
        string pinned = _vault.AddRsaKey(KeyName);
        string latest = _vault.AddRsaKey(KeyName);
        AzureKeyVaultSigningKeyProvider provider = CreateProvider(
            new AzureKeyVaultSigningKeyOptions { KeyName = KeyName, KeyVersion = pinned, Algorithm = SignatureAlgorithm.PS256 });
        var service = new AsymmetricSignatureService(provider);

        byte[] signature = await service.SignAsync(Data, KeyId);

        Assert.Equal([(KeyName, (string?)pinned)], _vault.GetKeyRequests);
        Assert.Equal([(KeyName, (string?)pinned)], _vault.CryptographyClientRequests);
        Assert.True(_vault.GetRsa(KeyName, pinned).VerifyData(Data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        Assert.False(_vault.GetRsa(KeyName, latest).VerifyData(Data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    }

    [Fact]
    public async Task GetSigningKeyAsync_NoVersionPinned_BindsCryptographyClientToResolvedLatestVersion()
    {
        _vault.AddRsaKey(KeyName);
        string latest = _vault.AddRsaKey(KeyName);
        AzureKeyVaultSigningKeyProvider provider = CreateProvider(SignatureAlgorithm.PS256);

        await provider.GetSigningKeyAsync(KeyId);

        Assert.Equal([(KeyName, (string?)null)], _vault.GetKeyRequests);
        Assert.Equal([(KeyName, (string?)latest)], _vault.CryptographyClientRequests);
    }

    [Fact]
    public async Task GetSigningKeyAsync_WithinRefreshInterval_ReadsKeyOnce()
    {
        _vault.AddRsaKey(KeyName);
        AzureKeyVaultSigningKeyProvider provider = CreateProvider(SignatureAlgorithm.PS256);

        SigningKey? first = await provider.GetSigningKeyAsync(KeyId);
        _time.Advance(RefreshInterval - TimeSpan.FromSeconds(1));
        SigningKey? second = await provider.GetSigningKeyAsync(KeyId);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, _vault.GetKeyCalls);
    }

    [Fact]
    public async Task GetSigningKeyAsync_AfterRefreshInterval_ReadsLatestVersionAgain()
    {
        _vault.AddRsaKey(KeyName);
        AzureKeyVaultSigningKeyProvider provider = CreateProvider(SignatureAlgorithm.PS256);
        var service = new AsymmetricSignatureService(provider);
        await service.SignAsync(Data, KeyId);

        string rotated = _vault.AddRsaKey(KeyName);
        _time.Advance(RefreshInterval);
        byte[] signature = await service.SignAsync(Data, KeyId);

        Assert.Equal(2, _vault.GetKeyCalls);
        Assert.Equal((KeyName, (string?)rotated), _vault.CryptographyClientRequests[^1]);
        Assert.True(_vault.GetRsa(KeyName, rotated).VerifyData(Data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        Assert.True(await service.VerifyAsync(Data, signature, KeyId));
    }

    [Theory]
    [InlineData(SignatureAlgorithm.PS256, 31)]
    [InlineData(SignatureAlgorithm.PS256, 48)]
    [InlineData(SignatureAlgorithm.RS384, 32)]
    [InlineData(SignatureAlgorithm.PS512, 0)]
    public async Task SignHashAsync_WrongDigestLength_ThrowsArgumentExceptionWithoutSigning(SignatureAlgorithm algorithm, int length)
    {
        _vault.AddRsaKey(KeyName);
        SigningKey? key = await CreateProvider(algorithm).GetSigningKeyAsync(KeyId);
        Assert.NotNull(key);

        await Assert.ThrowsAsync<ArgumentException>(async () => await key.SignHashAsync(new byte[length]));
        await Assert.ThrowsAsync<ArgumentException>(async () => await key.VerifyHashAsync(new byte[length], new byte[256]));
        Assert.Equal(0, _vault.SignCalls);
    }

    [Fact]
    public async Task SignHashAsync_EcKeyWrongDigestLength_ThrowsArgumentException()
    {
        _vault.AddEcKey(KeyName, ECCurve.NamedCurves.nistP384);
        SigningKey? key = await CreateProvider(SignatureAlgorithm.ES384).GetSigningKeyAsync(KeyId);
        Assert.NotNull(key);

        await Assert.ThrowsAsync<ArgumentException>(async () => await key.SignHashAsync(new byte[32]));
        Assert.Equal(0, _vault.SignCalls);
    }

    [Fact]
    public async Task SigningKey_Properties_ReflectConfiguration()
    {
        _vault.AddEcKey(KeyName, ECCurve.NamedCurves.nistP256);

        SigningKey? key = await CreateProvider(SignatureAlgorithm.ES256).GetSigningKeyAsync(KeyId);

        Assert.NotNull(key);
        Assert.Equal(KeyId, key.KeyId);
        Assert.Equal(SignatureAlgorithm.ES256, key.Algorithm);
        Assert.Equal(HashAlgorithmName.SHA256, key.HashAlgorithm);
    }

    [Fact]
    public async Task GetSigningKeyAsync_SeveralConfiguredKeys_ResolvesEachIndependently()
    {
        _vault.AddRsaKey("rsa-key");
        _vault.AddEcKey("ec-key", ECCurve.NamedCurves.nistP256);
        var options = new AzureKeyVaultSigningOptions
        {
            VaultUri = FakeKeyVault.VaultUri,
            RefreshInterval = RefreshInterval,
            Keys =
            {
                ["tokens"] = new AzureKeyVaultSigningKeyOptions { KeyName = "rsa-key", Algorithm = SignatureAlgorithm.PS384 },
                ["webhooks"] = new AzureKeyVaultSigningKeyOptions { KeyName = "ec-key", Algorithm = SignatureAlgorithm.ES256 },
            },
        };
        var service = new AsymmetricSignatureService(
            new AzureKeyVaultSigningKeyProvider(Microsoft.Extensions.Options.Options.Create(options), _vault.KeyClient, _time));

        byte[] tokenSignature = await service.SignAsync(Data, "tokens");
        byte[] webhookSignature = await service.SignAsync(Data, "webhooks");

        Assert.True(await service.VerifyAsync(Data, tokenSignature, "tokens"));
        Assert.True(await service.VerifyAsync(Data, webhookSignature, "webhooks"));
        Assert.False(await service.VerifyAsync(Data, tokenSignature, "webhooks"));
        Assert.False(await service.VerifyAsync(Data, webhookSignature, "tokens"));
        Assert.Equal(2, _vault.GetKeyCalls);
    }

    [Fact]
    public async Task AsymmetricSignatureService_UnknownKeyId_SignThrowsAndVerifyReturnsFalse()
    {
        _vault.AddRsaKey(KeyName);
        var service = new AsymmetricSignatureService(CreateProvider(SignatureAlgorithm.PS256));

        await Assert.ThrowsAsync<KeyNotFoundException>(async () => await service.SignAsync(Data, "unknown"));
        Assert.False(await service.VerifyAsync(Data, new byte[256], "unknown"));
        Assert.Equal(0, _vault.TotalCalls);
    }

    private AzureKeyVaultSigningKeyProvider CreateProvider(SignatureAlgorithm algorithm) =>
        CreateProvider(new AzureKeyVaultSigningKeyOptions { KeyName = KeyName, Algorithm = algorithm });

    private AzureKeyVaultSigningKeyProvider CreateProvider(AzureKeyVaultSigningKeyOptions key)
    {
        var options = new AzureKeyVaultSigningOptions
        {
            VaultUri = FakeKeyVault.VaultUri,
            RefreshInterval = RefreshInterval,
            Keys = { [KeyId] = key },
        };
        return new AzureKeyVaultSigningKeyProvider(Microsoft.Extensions.Options.Options.Create(options), _vault.KeyClient, _time);
    }

    private static byte[] Tamper(byte[] value)
    {
        byte[] copy = [.. value];
        copy[^1] ^= 0x01;
        return copy;
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

    private static (HashAlgorithmName Hash, RSASignaturePadding Padding) RsaParameters(SignatureAlgorithm algorithm) => algorithm switch
    {
        SignatureAlgorithm.PS256 => (HashAlgorithmName.SHA256, RSASignaturePadding.Pss),
        SignatureAlgorithm.PS384 => (HashAlgorithmName.SHA384, RSASignaturePadding.Pss),
        SignatureAlgorithm.PS512 => (HashAlgorithmName.SHA512, RSASignaturePadding.Pss),
        SignatureAlgorithm.RS256 => (HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
        SignatureAlgorithm.RS384 => (HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1),
        _ => (HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1),
    };
}
