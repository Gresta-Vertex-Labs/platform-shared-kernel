using SharedKernel.Cryptography.Signing;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeAsymmetricSignatureService"/> against <c>IAsymmetricSignatureService</c>'s contract: real
/// signatures under the algorithm bound to each key, and a record of every signing call.
/// </summary>
public sealed class FakeAsymmetricSignatureServiceTests
{
    private static readonly byte[] Data = "payload"u8.ToArray();

    [Fact]
    public async Task SignAsync_ThenVerifyAsync_RoundTrips()
    {
        var service = new FakeAsymmetricSignatureService();

        byte[] signature = await service.SignAsync(Data, "key-1");

        Assert.True(await service.VerifyAsync(Data, signature, "key-1"));
    }

    [Fact]
    public async Task VerifyAsync_TamperedData_ReturnsFalse()
    {
        var service = new FakeAsymmetricSignatureService();
        byte[] signature = await service.SignAsync(Data, "key-1");

        Assert.False(await service.VerifyAsync("tampered"u8.ToArray(), signature, "key-1"));
    }

    [Fact]
    public async Task VerifyAsync_TamperedSignature_ReturnsFalse()
    {
        var service = new FakeAsymmetricSignatureService();
        byte[] signature = await service.SignAsync(Data, "key-1");
        signature[0] ^= 0xFF;

        Assert.False(await service.VerifyAsync(Data, signature, "key-1"));
    }

    [Fact]
    public async Task VerifyAsync_DifferentKeyId_ReturnsFalse()
    {
        var service = new FakeAsymmetricSignatureService();
        byte[] signature = await service.SignAsync(Data, "key-1");

        Assert.False(await service.VerifyAsync(Data, signature, "key-2"));
    }

    [Theory]
    [MemberData(nameof(FakeSigningKeyProviderTests.AllAlgorithms), MemberType = typeof(FakeSigningKeyProviderTests))]
    public async Task SignAndVerify_UseTheAlgorithmBoundToTheKey(SignatureAlgorithm algorithm)
    {
        var service = new FakeAsymmetricSignatureService();
        service.KeyProvider.AddKey("key", algorithm);

        byte[] signature = await service.SignAsync(Data, "key");

        Assert.Equal(algorithm, await service.GetAlgorithmAsync("key"));
        Assert.True(await service.VerifyAsync(Data, signature, "key"));
    }

    [Fact]
    public async Task StreamOverloads_AgreeWithMemoryOverloads()
    {
        var service = new FakeAsymmetricSignatureService();

        byte[] fromStream = await service.SignAsync(new MemoryStream(Data), "key-1");
        byte[] fromMemory = await service.SignAsync(Data, "key-1");

        Assert.True(await service.VerifyAsync(Data, fromStream, "key-1"));
        Assert.True(await service.VerifyAsync(new MemoryStream(Data), fromMemory, "key-1"));
    }

    [Fact]
    public async Task SignAsync_UnknownKey_WithCreateKeysOnDemandDisabled_ThrowsKeyNotFound()
    {
        var service = new FakeAsymmetricSignatureService(new FakeSigningKeyProvider { CreateKeysOnDemand = false });

        await Assert.ThrowsAsync<KeyNotFoundException>(async () => await service.SignAsync(Data, "missing"));
        Assert.False(await service.VerifyAsync(Data, new byte[64], "missing"));
    }

    [Fact]
    public async Task Constructor_WithSuppliedKeyProvider_SignsWithThatProvidersKeys()
    {
        using var keys = new FakeSigningKeyProvider();
        SigningKey key = keys.AddKey("shared", SignatureAlgorithm.PS256);
        var service = new FakeAsymmetricSignatureService(keys);

        byte[] signature = await service.SignAsync(Data, "shared");

        Assert.Same(keys, service.KeyProvider);
        Assert.Equal(SignatureAlgorithm.PS256, key.Algorithm);
        Assert.True(await new AsymmetricSignatureService(keys).VerifyAsync(Data, signature, "shared"));
    }

    [Fact]
    public async Task SignedPayloads_RecordsEveryCall_InOrder()
    {
        var service = new FakeAsymmetricSignatureService();

        await service.SignAsync("a"u8.ToArray(), "key-1");
        await service.SignAsync("b"u8.ToArray(), "key-2");

        Assert.Equal(2, service.SignedPayloads.Count);
        Assert.Equal("key-1", service.SignedPayloads[0].KeyId);
        Assert.Equal("a"u8.ToArray(), service.SignedPayloads[0].Data);
        Assert.Equal("key-2", service.SignedPayloads[1].KeyId);
        Assert.Equal("b"u8.ToArray(), service.SignedPayloads[1].Data);
    }

    [Fact]
    public async Task SignAsync_NullKeyId_Throws()
    {
        var service = new FakeAsymmetricSignatureService();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.SignAsync(Data, null!));
    }
}
