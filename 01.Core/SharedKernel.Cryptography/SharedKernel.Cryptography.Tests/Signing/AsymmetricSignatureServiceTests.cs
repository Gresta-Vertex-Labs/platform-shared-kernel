using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Cryptography.Tests.Signing;

[Collection(SigningKeyCollection.Name)]
public sealed class AsymmetricSignatureServiceTests(SigningKeyMaterial keys)
{
    private static readonly byte[] Data = Encoding.UTF8.GetBytes("{\"amount\":\"125.00\",\"currency\":\"EUR\"}");

    [Theory]
    [MemberData(nameof(SigningKeyMaterial.AllAlgorithms), MemberType = typeof(SigningKeyMaterial))]
    public async Task SignAsync_VerifyAsync_RoundTrips(SignatureAlgorithm algorithm)
    {
        AsymmetricSignatureService service = CreateService(keys.CreateKey("k", algorithm));

        byte[] signature = await service.SignAsync(Data, "k");

        Assert.True(await service.VerifyAsync(Data, signature, "k"));
    }

    [Theory]
    [MemberData(nameof(SigningKeyMaterial.AllAlgorithms), MemberType = typeof(SigningKeyMaterial))]
    public async Task VerifyAsync_TamperedData_ReturnsFalse(SignatureAlgorithm algorithm)
    {
        AsymmetricSignatureService service = CreateService(keys.CreateKey("k", algorithm));
        byte[] signature = await service.SignAsync(Data, "k");
        byte[] tampered = [.. Data];
        tampered[^2] ^= 0x01;

        Assert.False(await service.VerifyAsync(tampered, signature, "k"));
    }

    [Theory]
    [MemberData(nameof(SigningKeyMaterial.AllAlgorithms), MemberType = typeof(SigningKeyMaterial))]
    public async Task VerifyAsync_TamperedSignature_ReturnsFalse(SignatureAlgorithm algorithm)
    {
        AsymmetricSignatureService service = CreateService(keys.CreateKey("k", algorithm));
        byte[] signature = await service.SignAsync(Data, "k");
        signature[signature.Length / 2] ^= 0x01;

        Assert.False(await service.VerifyAsync(Data, signature, "k"));
    }

    [Theory]
    [MemberData(nameof(SigningKeyMaterial.AllAlgorithms), MemberType = typeof(SigningKeyMaterial))]
    public async Task VerifyAsync_SignatureFromDifferentKey_ReturnsFalse(SignatureAlgorithm algorithm)
    {
        AsymmetricSignatureService service = CreateService(
            keys.CreateKey("a", algorithm),
            keys.CreateKey("b", algorithm, second: true));
        byte[] signature = await service.SignAsync(Data, "a");

        Assert.False(await service.VerifyAsync(Data, signature, "b"));
    }

    [Theory]
    [InlineData(SignatureAlgorithm.PS256, SignatureAlgorithm.RS256)]
    [InlineData(SignatureAlgorithm.RS256, SignatureAlgorithm.PS256)]
    [InlineData(SignatureAlgorithm.PS256, SignatureAlgorithm.PS384)]
    [InlineData(SignatureAlgorithm.RS512, SignatureAlgorithm.RS256)]
    public async Task VerifyAsync_SameRsaKeyBoundToOtherAlgorithm_ReturnsFalse(SignatureAlgorithm signedWith, SignatureAlgorithm verifiedWith)
    {
        AsymmetricSignatureService service = CreateService(
            SigningKey.FromRsa("signer", keys.Rsa(), signedWith, ownsKey: false),
            SigningKey.FromRsa("verifier", keys.Rsa(), verifiedWith, ownsKey: false));
        byte[] signature = await service.SignAsync(Data, "signer");

        Assert.False(await service.VerifyAsync(Data, signature, "verifier"));
    }

    [Theory]
    [InlineData(SignatureAlgorithm.RS256)]
    [InlineData(SignatureAlgorithm.RS384)]
    [InlineData(SignatureAlgorithm.RS512)]
    public async Task SignAsync_RsAlgorithm_ProducesStandardPkcs1Signature(SignatureAlgorithm algorithm)
    {
        AsymmetricSignatureService service = CreateService(keys.CreateKey("k", algorithm));

        byte[] signature = await service.SignAsync(Data, "k");

        Assert.Equal(256, signature.Length);
        Assert.True(keys.Rsa().VerifyData(Data, signature, SigningKey.GetHashAlgorithm(algorithm), RSASignaturePadding.Pkcs1));
        Assert.False(keys.Rsa().VerifyData(Data, signature, SigningKey.GetHashAlgorithm(algorithm), RSASignaturePadding.Pss));
        Assert.Equal(keys.Rsa().SignData(Data, SigningKey.GetHashAlgorithm(algorithm), RSASignaturePadding.Pkcs1), signature);
    }

    [Theory]
    [InlineData(SignatureAlgorithm.PS256)]
    [InlineData(SignatureAlgorithm.PS384)]
    [InlineData(SignatureAlgorithm.PS512)]
    public async Task SignAsync_PsAlgorithm_ProducesStandardPssSignature(SignatureAlgorithm algorithm)
    {
        AsymmetricSignatureService service = CreateService(keys.CreateKey("k", algorithm));

        byte[] signature = await service.SignAsync(Data, "k");

        Assert.True(keys.Rsa().VerifyData(Data, signature, SigningKey.GetHashAlgorithm(algorithm), RSASignaturePadding.Pss));
        Assert.False(keys.Rsa().VerifyData(Data, signature, SigningKey.GetHashAlgorithm(algorithm), RSASignaturePadding.Pkcs1));
    }

    [Theory]
    [InlineData(SignatureAlgorithm.ES256, 64)]
    [InlineData(SignatureAlgorithm.ES384, 96)]
    [InlineData(SignatureAlgorithm.ES512, 132)]
    public async Task SignAsync_EsAlgorithm_ProducesIeeeP1363Signature(SignatureAlgorithm algorithm, int expectedLength)
    {
        AsymmetricSignatureService service = CreateService(keys.CreateKey("k", algorithm));

        byte[] signature = await service.SignAsync(Data, "k");

        Assert.Equal(expectedLength, signature.Length);
        Assert.True(keys.Ecdsa(algorithm).VerifyData(
            Data,
            signature,
            SigningKey.GetHashAlgorithm(algorithm),
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Theory]
    [MemberData(nameof(SigningKeyMaterial.EcdsaAlgorithms), MemberType = typeof(SigningKeyMaterial))]
    public async Task SignAsync_DerFormatKey_ProducesRfc3279Signature(SignatureAlgorithm algorithm)
    {
        AsymmetricSignatureService service = CreateService(
            keys.CreateKey("k", algorithm, format: DSASignatureFormat.Rfc3279DerSequence));

        byte[] signature = await service.SignAsync(Data, "k");

        Assert.Equal(0x30, signature[0]);
        Assert.True(keys.Ecdsa(algorithm).VerifyData(Data, signature, SigningKey.GetHashAlgorithm(algorithm), DSASignatureFormat.Rfc3279DerSequence));
        Assert.True(await service.VerifyAsync(Data, signature, "k"));
    }

    [Theory]
    [MemberData(nameof(SigningKeyMaterial.EcdsaAlgorithms), MemberType = typeof(SigningKeyMaterial))]
    public async Task VerifyAsync_ExternallyProducedIeeeP1363Signature_ReturnsTrue(SignatureAlgorithm algorithm)
    {
        AsymmetricSignatureService service = CreateService(keys.CreateKey("k", algorithm));
        byte[] signature = keys.Ecdsa(algorithm).SignData(Data, SigningKey.GetHashAlgorithm(algorithm), DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Assert.True(await service.VerifyAsync(Data, signature, "k"));
    }

    [Theory]
    [MemberData(nameof(SigningKeyMaterial.AllAlgorithms), MemberType = typeof(SigningKeyMaterial))]
    public async Task StreamOverloads_InteroperateWithMemoryOverloads(SignatureAlgorithm algorithm)
    {
        AsymmetricSignatureService service = CreateService(keys.CreateKey("k", algorithm));
        byte[] large = RandomNumberGenerator.GetBytes(300_000);

        byte[] fromStream = await service.SignAsync(new MemoryStream(large), "k");
        byte[] fromMemory = await service.SignAsync(large, "k");

        Assert.True(await service.VerifyAsync(large, fromStream, "k"));
        Assert.True(await service.VerifyAsync(new MemoryStream(large), fromMemory, "k"));
        Assert.False(await service.VerifyAsync(new MemoryStream(Data), fromMemory, "k"));
    }

    [Theory]
    [InlineData(SignatureAlgorithm.RS256)]
    [InlineData(SignatureAlgorithm.RS512)]
    public async Task StreamOverload_DeterministicAlgorithm_ProducesSameSignatureAsMemoryOverload(SignatureAlgorithm algorithm)
    {
        AsymmetricSignatureService service = CreateService(keys.CreateKey("k", algorithm));

        Assert.Equal(await service.SignAsync(Data, "k"), await service.SignAsync(new MemoryStream(Data), "k"));
    }

    [Fact]
    public async Task SignAsync_UnknownKey_ThrowsKeyNotFound()
    {
        AsymmetricSignatureService service = CreateService(keys.CreateKey("k", SignatureAlgorithm.ES256));

        await Assert.ThrowsAsync<KeyNotFoundException>(async () => await service.SignAsync(Data, "missing"));
        await Assert.ThrowsAsync<KeyNotFoundException>(async () => await service.SignAsync(new MemoryStream(Data), "missing"));
        await Assert.ThrowsAsync<KeyNotFoundException>(async () => await service.GetAlgorithmAsync("missing"));
    }

    [Fact]
    public async Task VerifyAsync_UnknownKey_ReturnsFalse()
    {
        AsymmetricSignatureService service = CreateService(keys.CreateKey("k", SignatureAlgorithm.ES256));
        byte[] signature = await service.SignAsync(Data, "k");

        Assert.False(await service.VerifyAsync(Data, signature, "missing"));
        Assert.False(await service.VerifyAsync(new MemoryStream(Data), signature, "missing"));
    }

    public static TheoryData<SignatureAlgorithm, int> MalformedSignatureCases()
    {
        var data = new TheoryData<SignatureAlgorithm, int>();
        foreach (SignatureAlgorithm algorithm in Enum.GetValues<SignatureAlgorithm>())
        {
            foreach (int length in new[] { 0, 1, 10, 64, 96, 132, 255, 256, 257, 1024 })
            {
                data.Add(algorithm, length);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MalformedSignatureCases))]
    public async Task VerifyAsync_RandomGarbageSignature_ReturnsFalse(SignatureAlgorithm algorithm, int length)
    {
        AsymmetricSignatureService service = CreateService(keys.CreateKey("k", algorithm));
        byte[] garbage = RandomNumberGenerator.GetBytes(length);

        Assert.False(await service.VerifyAsync(Data, garbage, "k"));
        Assert.False(await service.VerifyAsync(new MemoryStream(Data), garbage, "k"));
    }

    [Theory]
    [MemberData(nameof(SigningKeyMaterial.EcdsaAlgorithms), MemberType = typeof(SigningKeyMaterial))]
    public async Task VerifyAsync_MalformedDerSignature_ReturnsFalse(SignatureAlgorithm algorithm)
    {
        AsymmetricSignatureService service = CreateService(
            keys.CreateKey("k", algorithm, format: DSASignatureFormat.Rfc3279DerSequence));

        byte[][] malformed = [[], [0x30], [0x30, 0x81, 0xFF, 0x02], [0x02, 0x01, 0x01], [0x30, 0x06, 0x02, 0x01, 0x00, 0x02, 0x01, 0x00], RandomNumberGenerator.GetBytes(72)];
        foreach (byte[] garbage in malformed)
        {
            Assert.False(await service.VerifyAsync(Data, garbage, "k"));
        }
    }

    [Theory]
    [MemberData(nameof(SigningKeyMaterial.AllAlgorithms), MemberType = typeof(SigningKeyMaterial))]
    public async Task PublicKeyOnly_VerifiesButCannotSign(SignatureAlgorithm algorithm)
    {
        AsymmetricSignatureService signer = CreateService(keys.CreateKey("k", algorithm));
        byte[] signature = await signer.SignAsync(Data, "k");
        using SigningKey publicKey = CreatePublicOnlyKey("k", algorithm);
        AsymmetricSignatureService verifier = CreateService(publicKey);

        Assert.True(await verifier.VerifyAsync(Data, signature, "k"));
        await Assert.ThrowsAnyAsync<CryptographicException>(async () => await verifier.SignAsync(Data, "k"));
    }

    [Theory]
    [MemberData(nameof(SigningKeyMaterial.AllAlgorithms), MemberType = typeof(SigningKeyMaterial))]
    public async Task GetAlgorithmAsync_ReturnsKeyAlgorithm(SignatureAlgorithm algorithm)
    {
        AsymmetricSignatureService service = CreateService(keys.CreateKey("k", algorithm));

        Assert.Equal(algorithm, await service.GetAlgorithmAsync("k"));
    }

    [Fact]
    public async Task Members_PassCancellationTokenToProvider()
    {
        var provider = new RecordingSigningKeyProvider(keys.CreateKey("k", SignatureAlgorithm.ES256));
        var service = new AsymmetricSignatureService(provider);
        using var cts = new CancellationTokenSource();

        byte[] signature = await service.SignAsync(Data, "k", cts.Token);
        await service.SignAsync(new MemoryStream(Data), "k", cts.Token);
        await service.VerifyAsync(Data, signature, "k", cts.Token);
        await service.VerifyAsync(new MemoryStream(Data), signature, "k", cts.Token);
        await service.GetAlgorithmAsync("k", cts.Token);

        Assert.Equal(5, provider.Tokens.Count);
        Assert.All(provider.Tokens, token => Assert.Equal(cts.Token, token));
    }

    [Fact]
    public async Task NullArguments_Throw()
    {
        AsymmetricSignatureService service = CreateService(keys.CreateKey("k", SignatureAlgorithm.ES256));
        Stream? noStream = null;

        Assert.Throws<ArgumentNullException>(() => new AsymmetricSignatureService(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.SignAsync(Data, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.SignAsync(noStream!, "k"));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.VerifyAsync(Data, Data, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.VerifyAsync(noStream!, Data, "k"));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.GetAlgorithmAsync(null!));
    }

    private static AsymmetricSignatureService CreateService(params SigningKey[] signingKeys) =>
        new(new InMemorySigningKeyProvider(signingKeys));

    private SigningKey CreatePublicOnlyKey(string keyId, SignatureAlgorithm algorithm)
    {
        if (SigningKeyMaterial.IsRsa(algorithm))
        {
            var rsa = RSA.Create();
            rsa.ImportParameters(keys.Rsa().ExportParameters(includePrivateParameters: false));
            return SigningKey.FromRsa(keyId, rsa, algorithm);
        }

        var ecdsa = ECDsa.Create();
        ecdsa.ImportParameters(keys.Ecdsa(algorithm).ExportParameters(includePrivateParameters: false));
        return SigningKey.FromECDsa(keyId, ecdsa);
    }

    private sealed class RecordingSigningKeyProvider(SigningKey key) : ISigningKeyProvider
    {
        public List<CancellationToken> Tokens { get; } = [];

        public ValueTask<SigningKey?> GetSigningKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            return new(key.KeyId == keyId ? key : null);
        }
    }
}
