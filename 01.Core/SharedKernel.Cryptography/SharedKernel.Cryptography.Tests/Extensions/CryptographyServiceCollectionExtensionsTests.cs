using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Cryptography.Tests.Signing;
using SharedKernel.Cryptography.Tests.Symmetric;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Extensions;

public sealed class CryptographyServiceCollectionExtensionsTests
{
    private static IConfiguration EmptyConfiguration() =>
        new ConfigurationBuilder().Build();

    [Fact]
    public void AddSharedKernelCryptography_RegistersOneWayHasher()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());
        services.AddSingleton<IEncryptionKeyProvider>(new InMemoryEncryptionKeyProvider());
        services.AddSingleton<IAsymmetricKeyProvider>(new InMemoryAsymmetricKeyProvider());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<Pbkdf2OneWayHasher>(provider.GetRequiredService<IOneWayHasher>());
    }

    [Fact]
    public void AddSharedKernelCryptography_RegistersSymmetricEncryptionService()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());
        services.AddSingleton<IEncryptionKeyProvider>(new InMemoryEncryptionKeyProvider());
        services.AddSingleton<IAsymmetricKeyProvider>(new InMemoryAsymmetricKeyProvider());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<AesGcmEncryptionService>(provider.GetRequiredService<ISymmetricEncryptionService>());
    }

    [Fact]
    public void AddSharedKernelCryptography_RegistersDefaultAndKeyedAsymmetricSignatureServices()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());
        services.AddSingleton<IEncryptionKeyProvider>(new InMemoryEncryptionKeyProvider());
        services.AddSingleton<IAsymmetricKeyProvider>(new InMemoryAsymmetricKeyProvider());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<RsaSignatureService>(provider.GetRequiredService<IAsymmetricSignatureService>());
        Assert.IsType<RsaSignatureService>(
            provider.GetRequiredKeyedService<IAsymmetricSignatureService>(
                CryptographyServiceCollectionExtensions.RsaSignatureServiceKey));
        Assert.IsType<EcdsaSignatureService>(
            provider.GetRequiredKeyedService<IAsymmetricSignatureService>(
                CryptographyServiceCollectionExtensions.EcdsaSignatureServiceKey));
    }

    [Fact]
    public void AddSharedKernelCryptography_RegistersHmacSigner()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());
        services.AddSingleton<IEncryptionKeyProvider>(new InMemoryEncryptionKeyProvider());
        services.AddSingleton<IAsymmetricKeyProvider>(new InMemoryAsymmetricKeyProvider());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<HmacSha256Signer>(provider.GetRequiredService<IHmacSigner>());
    }

    [Fact]
    public void AddSharedKernelCryptography_RegistersSecureRandomGenerator()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());
        services.AddSingleton<IEncryptionKeyProvider>(new InMemoryEncryptionKeyProvider());
        services.AddSingleton<IAsymmetricKeyProvider>(new InMemoryAsymmetricKeyProvider());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<CryptoRandomGenerator>(provider.GetRequiredService<ISecureRandomGenerator>());
    }

    [Fact]
    public void AddSharedKernelCryptography_RegistersContentHasher()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());
        services.AddSingleton<IEncryptionKeyProvider>(new InMemoryEncryptionKeyProvider());
        services.AddSingleton<IAsymmetricKeyProvider>(new InMemoryAsymmetricKeyProvider());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<Sha256ContentHasher>(provider.GetRequiredService<IContentHasher>());
    }

    [Fact]
    public void AddSharedKernelCryptography_RegistersAllSixServices()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());
        services.AddSingleton<IEncryptionKeyProvider>(new InMemoryEncryptionKeyProvider());
        services.AddSingleton<IAsymmetricKeyProvider>(new InMemoryAsymmetricKeyProvider());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IOneWayHasher>());
        Assert.NotNull(provider.GetRequiredService<ISymmetricEncryptionService>());
        Assert.NotNull(provider.GetRequiredService<IAsymmetricSignatureService>());
        Assert.NotNull(provider.GetRequiredService<IHmacSigner>());
        Assert.NotNull(provider.GetRequiredService<ISecureRandomGenerator>());
        Assert.NotNull(provider.GetRequiredService<IContentHasher>());
    }

    [Fact]
    public void AddSharedKernelCryptography_DoesNotRegisterEncryptionKeyProvider()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IEncryptionKeyProvider>());
    }

    [Fact]
    public void AddSharedKernelCryptography_RegistersHotpGenerator()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());
        services.AddSingleton<IEncryptionKeyProvider>(new InMemoryEncryptionKeyProvider());
        services.AddSingleton<IAsymmetricKeyProvider>(new InMemoryAsymmetricKeyProvider());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<HotpGenerator>(provider.GetRequiredService<IHotpGenerator>());
    }

    [Fact]
    public void AddSharedKernelCryptography_RegistersTotpGenerator_GivenAnIClockIsAlsoRegistered()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());
        services.AddSingleton<IEncryptionKeyProvider>(new InMemoryEncryptionKeyProvider());
        services.AddSingleton<IAsymmetricKeyProvider>(new InMemoryAsymmetricKeyProvider());
        services.AddSingleton<IClock>(new SystemClock());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<TotpGenerator>(provider.GetRequiredService<ITotpGenerator>());
    }

    [Fact]
    public void AddSharedKernelCryptography_DoesNotRegisterTotpReplayGuard()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<ITotpReplayGuard>());
    }

    [Fact]
    public void AddSharedKernelCryptography_RegistersTotpVerifier_GivenIClockAndReplayGuardAreAlsoRegistered()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());
        services.AddSingleton<IEncryptionKeyProvider>(new InMemoryEncryptionKeyProvider());
        services.AddSingleton<IAsymmetricKeyProvider>(new InMemoryAsymmetricKeyProvider());
        services.AddSingleton<IClock>(new SystemClock());
        services.AddSingleton(Substitute.For<ITotpReplayGuard>());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<TotpVerifier>(provider.GetRequiredService<TotpVerifier>());
    }

    [Fact]
    public async Task InvalidCryptographyOptions_ThrowsAtHostStartup()
    {
        var invalidConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Cryptography:Pbkdf2Iterations"] = "-1",
            })
            .Build();

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSharedKernelCryptography(invalidConfig);
                services.AddSingleton<IEncryptionKeyProvider>(new InMemoryEncryptionKeyProvider());
                services.AddSingleton<IAsymmetricKeyProvider>(new InMemoryAsymmetricKeyProvider());
            })
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task Pbkdf2IterationsBelowNewMinimum_ThrowsAtHostStartup()
    {
        // P-512/WO-083: a configured value below the new 100,000 floor (but otherwise a
        // syntactically valid positive integer, unlike the pre-existing "-1" case above) must
        // also fail startup validation — closing the "iterations=1 passes validation" gap.
        var belowFloorConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Cryptography:Pbkdf2Iterations"] = "50000",
            })
            .Build();

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSharedKernelCryptography(belowFloorConfig);
                services.AddSingleton<IEncryptionKeyProvider>(new InMemoryEncryptionKeyProvider());
                services.AddSingleton<IAsymmetricKeyProvider>(new InMemoryAsymmetricKeyProvider());
            })
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public void AddSharedKernelCryptography_CalledTwice_RegistersEachServiceExactlyOnce()
    {
        // SK.01.P518 headline acceptance criterion: every registration in this method uses
        // TryAddSingleton/TryAddKeyedSingleton, so calling AddSharedKernelCryptography() twice
        // (e.g. two consuming packages each calling it) must never double-register any of its
        // eleven services.
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());
        services.AddSharedKernelCryptography(EmptyConfiguration());
        services.AddSingleton<IEncryptionKeyProvider>(new InMemoryEncryptionKeyProvider());
        services.AddSingleton<IAsymmetricKeyProvider>(new InMemoryAsymmetricKeyProvider());
        services.AddSingleton<IClock>(new SystemClock());
        services.AddSingleton(Substitute.For<ITotpReplayGuard>());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IOneWayHasher>());
        Assert.Single(provider.GetServices<ISymmetricEncryptionService>());
        Assert.Single(provider.GetServices<IHmacSigner>());
        Assert.Single(provider.GetServices<ISecureRandomGenerator>());
        Assert.Single(provider.GetServices<IContentHasher>());
        Assert.Single(provider.GetServices<IAsymmetricSignatureService>());
        Assert.Single(provider.GetServices<IHotpGenerator>());
        Assert.Single(provider.GetServices<ITotpGenerator>());
        Assert.Single(provider.GetServices<TotpVerifier>());
        Assert.Single(provider.GetKeyedServices<IAsymmetricSignatureService>(
            CryptographyServiceCollectionExtensions.RsaSignatureServiceKey));
        Assert.Single(provider.GetKeyedServices<IAsymmetricSignatureService>(
            CryptographyServiceCollectionExtensions.EcdsaSignatureServiceKey));
    }

    [Fact]
    public void AddSharedKernelCryptography_ConsumerFakeRegisteredFirst_WinsOverPlatformDefault()
    {
        var services = new ServiceCollection();
        var fake = new FakeOneWayHasher();

        services.AddSingleton<IOneWayHasher>(fake);
        services.AddSharedKernelCryptography(EmptyConfiguration());
        services.AddSingleton<IEncryptionKeyProvider>(new InMemoryEncryptionKeyProvider());
        services.AddSingleton<IAsymmetricKeyProvider>(new InMemoryAsymmetricKeyProvider());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(fake, provider.GetRequiredService<IOneWayHasher>());
    }

    private sealed class FakeOneWayHasher : IOneWayHasher
    {
        public string Hash(string secret) => secret;

        public HashVerificationResult Verify(string hash, string secret) =>
            hash == secret ? HashVerificationResult.Success : HashVerificationResult.Failed;
    }

    [Fact]
    public void NullServices_Throws()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() => services!.AddSharedKernelCryptography(EmptyConfiguration()));
    }

    [Fact]
    public void NullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddSharedKernelCryptography(null!));
    }
}
