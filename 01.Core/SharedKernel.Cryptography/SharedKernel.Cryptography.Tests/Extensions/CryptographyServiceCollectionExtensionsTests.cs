using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Cryptography.Tests.Signing;
using SharedKernel.Cryptography.Tests.Symmetric;
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
    public void AddSharedKernelCryptography_DoesNotRegisterEncryptionKeyProvider()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IEncryptionKeyProvider>());
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
