using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeCryptographyServiceCollectionExtensions.AddFakeCryptography"/>'s DI
/// registration shape, plus confirms the manual per-type registration alternative demonstrated by
/// the individual fake test files still works standalone. Proven exclusively in
/// <c>SharedKernel.Testing.SelfTests</c> — see <c>16.Testing/state-map.md</c> T-58.
/// </summary>
public sealed class FakeCryptographyServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFakeCryptography_AllEightSingletonRegistrations_Resolve()
    {
        var provider = BuildProvider();

        Assert.IsType<FakeOneWayHasher>(provider.GetRequiredService<IOneWayHasher>());
        Assert.IsType<FakeEncryptionKeyProvider>(provider.GetRequiredService<IEncryptionKeyProvider>());
        Assert.IsType<FakeSymmetricEncryptionService>(provider.GetRequiredService<ISymmetricEncryptionService>());
        Assert.IsType<FakeAsymmetricKeyProvider>(provider.GetRequiredService<IAsymmetricKeyProvider>());
        Assert.IsType<FakeAsymmetricSignatureService>(provider.GetRequiredService<IAsymmetricSignatureService>());
        Assert.IsType<FakeHmacSigner>(provider.GetRequiredService<IHmacSigner>());
        Assert.IsType<FakeSecureRandomGenerator>(provider.GetRequiredService<ISecureRandomGenerator>());
        Assert.IsType<FakeContentHasher>(provider.GetRequiredService<IContentHasher>());
    }

    [Fact]
    public void AddFakeCryptography_UnkeyedAndBothKeyedAsymmetricSignatureServiceSlots_AllResolve_AndWork()
    {
        var provider = BuildProvider();
        var data = "payload"u8.ToArray();

        var unkeyed = provider.GetRequiredService<IAsymmetricSignatureService>();
        var rsaKeyed = provider.GetRequiredKeyedService<IAsymmetricSignatureService>(
            CryptographyServiceCollectionExtensions.RsaSignatureServiceKey);
        var ecdsaKeyed = provider.GetRequiredKeyedService<IAsymmetricSignatureService>(
            CryptographyServiceCollectionExtensions.EcdsaSignatureServiceKey);

        Assert.IsType<FakeAsymmetricSignatureService>(unkeyed);
        Assert.IsType<FakeAsymmetricSignatureService>(rsaKeyed);
        Assert.IsType<FakeAsymmetricSignatureService>(ecdsaKeyed);

        var signature = unkeyed.Sign(data, "some-key");
        Assert.True(unkeyed.Verify(data, signature, "some-key"));

        var rsaSignature = rsaKeyed.Sign(data, "some-key");
        Assert.True(rsaKeyed.Verify(data, rsaSignature, "some-key"));

        var ecdsaSignature = ecdsaKeyed.Sign(data, "some-key");
        Assert.True(ecdsaKeyed.Verify(data, ecdsaSignature, "some-key"));
    }

    [Fact]
    public void AddFakeCryptography_AllRegistrations_ResolveTheSameInstanceAcrossCalls()
    {
        var provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<IOneWayHasher>(), provider.GetRequiredService<IOneWayHasher>());
        Assert.Same(
            provider.GetRequiredService<IAsymmetricSignatureService>(),
            provider.GetRequiredService<IAsymmetricSignatureService>());
        Assert.Same(
            provider.GetRequiredKeyedService<IAsymmetricSignatureService>(CryptographyServiceCollectionExtensions.RsaSignatureServiceKey),
            provider.GetRequiredKeyedService<IAsymmetricSignatureService>(CryptographyServiceCollectionExtensions.RsaSignatureServiceKey));
    }

    [Fact]
    public void AddFakeCryptography_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddFakeCryptography());

    [Fact]
    public void ManualPerTypeRegistration_StillWorksStandalone_WithoutAddFakeCryptography()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOneWayHasher, FakeOneWayHasher>();
        services.AddSingleton<IEncryptionKeyProvider, FakeEncryptionKeyProvider>();
        services.AddSingleton<ISymmetricEncryptionService, FakeSymmetricEncryptionService>();
        services.AddSingleton<IAsymmetricKeyProvider, FakeAsymmetricKeyProvider>();
        services.AddSingleton<IAsymmetricSignatureService, FakeAsymmetricSignatureService>();
        services.AddSingleton<IHmacSigner, FakeHmacSigner>();
        services.AddSingleton<ISecureRandomGenerator, FakeSecureRandomGenerator>();
        services.AddSingleton<IContentHasher, FakeContentHasher>();

        var provider = services.BuildServiceProvider();

        Assert.IsType<FakeOneWayHasher>(provider.GetRequiredService<IOneWayHasher>());
        Assert.IsType<FakeEncryptionKeyProvider>(provider.GetRequiredService<IEncryptionKeyProvider>());
        Assert.IsType<FakeSymmetricEncryptionService>(provider.GetRequiredService<ISymmetricEncryptionService>());
        Assert.IsType<FakeAsymmetricKeyProvider>(provider.GetRequiredService<IAsymmetricKeyProvider>());
        Assert.IsType<FakeAsymmetricSignatureService>(provider.GetRequiredService<IAsymmetricSignatureService>());
        Assert.IsType<FakeHmacSigner>(provider.GetRequiredService<IHmacSigner>());
        Assert.IsType<FakeSecureRandomGenerator>(provider.GetRequiredService<ISecureRandomGenerator>());
        Assert.IsType<FakeContentHasher>(provider.GetRequiredService<IContentHasher>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddFakeCryptography();
        return services.BuildServiceProvider();
    }
}
