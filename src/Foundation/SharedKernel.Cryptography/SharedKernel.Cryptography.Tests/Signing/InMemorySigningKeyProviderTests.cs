using System.Security.Cryptography;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Cryptography.Tests.Signing;

[Collection(SigningKeyCollection.Name)]
public sealed class InMemorySigningKeyProviderTests(SigningKeyMaterial keys)
{
    [Fact]
    public async Task GetSigningKeyAsync_KnownId_ReturnsKey()
    {
        SigningKey a = keys.CreateKey("a", SignatureAlgorithm.ES256);
        SigningKey b = keys.CreateKey("b", SignatureAlgorithm.PS256);
        var provider = new InMemorySigningKeyProvider([a, b]);

        Assert.Same(a, await provider.GetSigningKeyAsync("a"));
        Assert.Same(b, await provider.GetSigningKeyAsync("b"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("A")]
    [InlineData("")]
    public async Task GetSigningKeyAsync_UnknownId_ReturnsNull(string keyId)
    {
        var provider = new InMemorySigningKeyProvider([keys.CreateKey("a", SignatureAlgorithm.ES256)]);

        Assert.Null(await provider.GetSigningKeyAsync(keyId));
    }

    [Fact]
    public async Task GetSigningKeyAsync_NullId_Throws()
    {
        var provider = new InMemorySigningKeyProvider([]);

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.GetSigningKeyAsync(null!));
    }

    [Fact]
    public void Constructor_DuplicateIds_Throws()
    {
        Assert.Throws<ArgumentException>(() => new InMemorySigningKeyProvider(
        [
            keys.CreateKey("same", SignatureAlgorithm.ES256),
            keys.CreateKey("same", SignatureAlgorithm.PS256),
        ]));
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new InMemorySigningKeyProvider(null!));
        Assert.Throws<ArgumentNullException>(() => new InMemorySigningKeyProvider([keys.CreateKey("a", SignatureAlgorithm.ES256), null!]));
    }

    [Fact]
    public async Task Dispose_DisposesEveryKey()
    {
        SigningKey ecdsaKey = SigningKey.FromECDsa("ec", ECDsa.Create(ECCurve.NamedCurves.nistP256));
        SigningKey rsaKey = SigningKey.FromRsa("rsa", RSA.Create(2048), SignatureAlgorithm.RS256);
        var provider = new InMemorySigningKeyProvider([ecdsaKey, rsaKey]);

        provider.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await ecdsaKey.SignHashAsync(new byte[32]));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await rsaKey.SignHashAsync(new byte[32]));
    }
}
