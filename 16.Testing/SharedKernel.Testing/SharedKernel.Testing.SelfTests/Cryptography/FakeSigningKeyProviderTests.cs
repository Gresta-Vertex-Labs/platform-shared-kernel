using System.Security.Cryptography;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeSigningKeyProvider"/> against <c>ISigningKeyProvider</c>'s contract: keys are real, bound to
/// one algorithm, stable per key id, and owned (and disposed) by the provider.
/// </summary>
public sealed class FakeSigningKeyProviderTests
{
    public static TheoryData<SignatureAlgorithm> AllAlgorithms() =>
    [
        SignatureAlgorithm.PS256, SignatureAlgorithm.PS384, SignatureAlgorithm.PS512,
        SignatureAlgorithm.RS256, SignatureAlgorithm.RS384, SignatureAlgorithm.RS512,
        SignatureAlgorithm.ES256, SignatureAlgorithm.ES384, SignatureAlgorithm.ES512,
    ];

    [Fact]
    public async Task GetSigningKeyAsync_SameKeyId_ReturnsTheSameInstance()
    {
        using var provider = new FakeSigningKeyProvider();

        var first = await provider.GetSigningKeyAsync("signing-key");
        var second = await provider.GetSigningKeyAsync("signing-key");

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetSigningKeyAsync_UnknownKeyId_CreatesAnEs256KeyOnDemandByDefault()
    {
        using var provider = new FakeSigningKeyProvider();

        var key = await provider.GetSigningKeyAsync("on-demand");

        Assert.True(provider.CreateKeysOnDemand);
        Assert.NotNull(key);
        Assert.Equal("on-demand", key.KeyId);
        Assert.Equal(SignatureAlgorithm.ES256, key.Algorithm);
    }

    [Fact]
    public async Task GetSigningKeyAsync_OnDemand_UsesTheConfiguredDefaultAlgorithm()
    {
        using var provider = new FakeSigningKeyProvider { DefaultAlgorithm = SignatureAlgorithm.PS384 };

        var key = await provider.GetSigningKeyAsync("rsa-on-demand");

        Assert.Equal(SignatureAlgorithm.PS384, key!.Algorithm);
    }

    [Fact]
    public async Task GetSigningKeyAsync_UnknownKeyId_WithCreateKeysOnDemandDisabled_ReturnsNull()
    {
        using var provider = new FakeSigningKeyProvider { CreateKeysOnDemand = false };

        Assert.Null(await provider.GetSigningKeyAsync("missing"));
    }

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public async Task AddKey_EveryAlgorithm_CreatesAWorkingKeyReturnedByGetSigningKeyAsync(SignatureAlgorithm algorithm)
    {
        using var provider = new FakeSigningKeyProvider { CreateKeysOnDemand = false };

        SigningKey added = provider.AddKey("key", algorithm);
        byte[] hash = CryptographicOperations.HashData(added.HashAlgorithm, "payload"u8);
        byte[] signature = await added.SignHashAsync(hash);

        Assert.Same(added, await provider.GetSigningKeyAsync("key"));
        Assert.Equal(algorithm, added.Algorithm);
        Assert.True(await added.VerifyHashAsync(hash, signature));
    }

    [Fact]
    public async Task DifferentKeyIds_HoldDifferentKeyPairs()
    {
        using var provider = new FakeSigningKeyProvider();
        var keyA = await provider.GetSigningKeyAsync("key-a");
        var keyB = await provider.GetSigningKeyAsync("key-b");
        byte[] hash = SHA256.HashData("payload"u8);

        byte[] signature = await keyA!.SignHashAsync(hash);

        Assert.False(await keyB!.VerifyHashAsync(hash, signature));
    }

    [Fact]
    public async Task AddKey_ReplacingAnExistingKeyId_DisposesThePreviousKey()
    {
        using var provider = new FakeSigningKeyProvider();
        SigningKey previous = provider.AddKey("key", SignatureAlgorithm.ES256);

        SigningKey replacement = provider.AddKey("key", SignatureAlgorithm.ES256);

        Assert.NotSame(previous, replacement);
        Assert.Same(replacement, await provider.GetSigningKeyAsync("key"));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await previous.SignHashAsync(SHA256.HashData("payload"u8)));
    }

    [Fact]
    public async Task Dispose_DisposesEveryKey()
    {
        var provider = new FakeSigningKeyProvider();
        SigningKey ecdsa = provider.AddKey("ecdsa", SignatureAlgorithm.ES256);
        SigningKey rsa = provider.AddKey("rsa", SignatureAlgorithm.PS256);

        provider.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await ecdsa.SignHashAsync(SHA256.HashData("payload"u8)));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await rsa.SignHashAsync(SHA256.HashData("payload"u8)));
    }

    [Fact]
    public async Task GetSigningKeyAsync_NullKeyId_Throws()
    {
        using var provider = new FakeSigningKeyProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.GetSigningKeyAsync(null!));
    }
}
