using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeCryptographyServiceCollectionExtensions.AddFakeCryptography"/>'s registration shape: every
/// cryptography contract resolves to its fake, and the contracts that share state resolve to the same instance.
/// </summary>
public sealed class FakeCryptographyServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFakeCryptography_EveryContract_ResolvesToItsFake()
    {
        using var provider = BuildProvider();

        Assert.IsType<FakeOneWayHasher>(provider.GetRequiredService<IOneWayHasher>());
        Assert.IsType<FakeSecureRandomGenerator>(provider.GetRequiredService<ISecureRandomGenerator>());
        Assert.IsType<FakeContentHasher>(provider.GetRequiredService<IContentHasher>());
        Assert.IsType<FakeHmacSigner>(provider.GetRequiredService<IHmacSigner>());
        Assert.IsType<FakeEncryptionKeyProvider>(provider.GetRequiredService<IEncryptionKeyProvider>());
        Assert.IsType<FakeEncryptionKeyProvider>(provider.GetRequiredService<ISynchronousEncryptionKeyProvider>());
        Assert.IsType<FakeSymmetricEncryptionService>(provider.GetRequiredService<ISymmetricEncryptionService>());
        Assert.IsType<FakeSymmetricEncryptionService>(provider.GetRequiredService<ISynchronousSymmetricEncryptionService>());
        Assert.IsType<FakeEnvelopeEncryptionProvider>(provider.GetRequiredService<IEnvelopeEncryptionProvider>());
        Assert.IsType<EnvelopeEncryptionService>(provider.GetRequiredService<IEnvelopeEncryptionService>());
        Assert.IsType<FakeSigningKeyProvider>(provider.GetRequiredService<ISigningKeyProvider>());
        Assert.IsType<FakeAsymmetricSignatureService>(provider.GetRequiredService<IAsymmetricSignatureService>());
        Assert.IsType<FakeTotpReplayGuard>(provider.GetRequiredService<ITotpReplayGuard>());
    }

    [Fact]
    public void AddFakeCryptography_OneKeyProviderBacksBothSymmetricServices()
    {
        using var provider = BuildProvider();
        var keys = provider.GetRequiredService<FakeEncryptionKeyProvider>();
        var service = provider.GetRequiredService<FakeSymmetricEncryptionService>();

        Assert.Same(keys, provider.GetRequiredService<IEncryptionKeyProvider>());
        Assert.Same(keys, provider.GetRequiredService<ISynchronousEncryptionKeyProvider>());
        Assert.Same(keys, service.KeyProvider);
        Assert.Same(service, provider.GetRequiredService<ISymmetricEncryptionService>());
        Assert.Same(service, provider.GetRequiredService<ISynchronousSymmetricEncryptionService>());
    }

    [Fact]
    public void AddFakeCryptography_RotatingTheResolvedKeyProvider_ChangesTheKeyTheServiceEncryptsWith()
    {
        using var provider = BuildProvider();
        var keys = provider.GetRequiredService<FakeEncryptionKeyProvider>();
        var service = provider.GetRequiredService<ISynchronousSymmetricEncryptionService>();

        keys.AddKey("v2");
        keys.SetCurrentKey("v2");

        Assert.Equal("v2", service.Encrypt("data"u8, []).KeyId);
    }

    [Fact]
    public async Task AddFakeCryptography_SigningServiceUsesTheRegisteredSigningKeyProvider()
    {
        using var provider = BuildProvider();
        var keys = provider.GetRequiredService<FakeSigningKeyProvider>();
        keys.AddKey("registered", SignatureAlgorithm.PS256);
        var service = provider.GetRequiredService<IAsymmetricSignatureService>();

        byte[] signature = await service.SignAsync("payload"u8.ToArray(), "registered");

        Assert.Same(keys, provider.GetRequiredService<ISigningKeyProvider>());
        Assert.Same(keys, provider.GetRequiredService<FakeAsymmetricSignatureService>().KeyProvider);
        Assert.Equal(SignatureAlgorithm.PS256, await service.GetAlgorithmAsync("registered"));
        Assert.True(await service.VerifyAsync("payload"u8.ToArray(), signature, "registered"));
    }

    [Fact]
    public async Task AddFakeCryptography_EnvelopeEncryptionService_RoundTripsOverTheFakeProvider()
    {
        using var provider = BuildProvider();
        var service = provider.GetRequiredService<IEnvelopeEncryptionService>();
        var envelopeProvider = provider.GetRequiredService<FakeEnvelopeEncryptionProvider>();

        EnvelopePayload payload = await service.EncryptAsync("secret"u8.ToArray(), "aad"u8.ToArray());
        var decrypted = await service.DecryptAsync(payload, "aad"u8.ToArray());

        Assert.Same(envelopeProvider, provider.GetRequiredService<IEnvelopeEncryptionProvider>());
        Assert.True(decrypted.IsSuccess);
        Assert.Equal("secret"u8.ToArray(), decrypted.Value);
        Assert.Equal(1, envelopeProvider.GenerateCallCount);
        Assert.Equal(1, envelopeProvider.UnwrapCallCount);
    }

    [Fact]
    public void AddFakeCryptography_EveryRegistration_IsASingleton()
    {
        using var provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<IOneWayHasher>(), provider.GetRequiredService<IOneWayHasher>());
        Assert.Same(provider.GetRequiredService<IHmacSigner>(), provider.GetRequiredService<IHmacSigner>());
        Assert.Same(
            provider.GetRequiredService<IAsymmetricSignatureService>(),
            provider.GetRequiredService<IAsymmetricSignatureService>());
        Assert.Same(provider.GetRequiredService<ITotpReplayGuard>(), provider.GetRequiredService<ITotpReplayGuard>());
    }

    [Fact]
    public void AddFakeCryptography_ReplacesEarlierRegistrations()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHmacSigner, HmacSha256Signer>();
        services.AddSingleton<ISecureRandomGenerator, SecureRandomGenerator>();

        services.AddFakeCryptography();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<FakeHmacSigner>(provider.GetRequiredService<IHmacSigner>());
        Assert.IsType<FakeSecureRandomGenerator>(provider.GetRequiredService<ISecureRandomGenerator>());
    }

    [Fact]
    public void AddFakeCryptography_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddFakeCryptography());

    [Fact]
    public void ManualPerTypeRegistration_WorksWithoutAddFakeCryptography()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOneWayHasher, FakeOneWayHasher>();
        services.AddSingleton<FakeEncryptionKeyProvider>();
        services.AddSingleton<ISymmetricEncryptionService>(
            sp => new FakeSymmetricEncryptionService(sp.GetRequiredService<FakeEncryptionKeyProvider>()));
        services.AddSingleton<ISigningKeyProvider, FakeSigningKeyProvider>();
        services.AddSingleton<IAsymmetricSignatureService, FakeAsymmetricSignatureService>();
        services.AddSingleton<IHmacSigner, FakeHmacSigner>();
        services.AddSingleton<ISecureRandomGenerator, FakeSecureRandomGenerator>();
        services.AddSingleton<IContentHasher, FakeContentHasher>();
        services.AddSingleton<ITotpReplayGuard, FakeTotpReplayGuard>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<FakeOneWayHasher>(provider.GetRequiredService<IOneWayHasher>());
        Assert.IsType<FakeSymmetricEncryptionService>(provider.GetRequiredService<ISymmetricEncryptionService>());
        Assert.IsType<FakeSigningKeyProvider>(provider.GetRequiredService<ISigningKeyProvider>());
        Assert.IsType<FakeAsymmetricSignatureService>(provider.GetRequiredService<IAsymmetricSignatureService>());
        Assert.IsType<FakeHmacSigner>(provider.GetRequiredService<IHmacSigner>());
        Assert.IsType<FakeSecureRandomGenerator>(provider.GetRequiredService<ISecureRandomGenerator>());
        Assert.IsType<FakeContentHasher>(provider.GetRequiredService<IContentHasher>());
        Assert.IsType<FakeTotpReplayGuard>(provider.GetRequiredService<ITotpReplayGuard>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddFakeCryptography();
        return services.BuildServiceProvider();
    }
}
