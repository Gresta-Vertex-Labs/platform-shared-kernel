using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.KeyVault.Azure.Extensions;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Symmetric;
using Xunit;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests.Extensions;

public sealed class AzureKeyVaultCryptographyServiceCollectionExtensionsTests
{
    private static IConfiguration ValidConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SharedKernel:Cryptography:KeyVault:Azure:VaultUri"] = "https://my-vault.vault.azure.net/",
            ["SharedKernel:Cryptography:KeyVault:Azure:CurrentKeyId"] = "primary",
            ["SharedKernel:Cryptography:KeyVault:Azure:KeyNames:primary"] = "tenant-data-key",
        })
        .Build();

    [Fact]
    public void NullServices_Throws()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(
            () => services!.AddSharedKernelAzureKeyVaultCryptography(ValidConfiguration()));
    }

    [Fact]
    public void NullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(
            () => services.AddSharedKernelAzureKeyVaultCryptography(null!));
    }

    [Fact]
    public void ValidConfiguration_RegistersSameSingletonInstance_ForBothServiceTypes()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelAzureKeyVaultCryptography(ValidConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();

        var asKeyProvider = provider.GetRequiredService<IEncryptionKeyProvider>();
        var asEnvelopeProvider = provider.GetRequiredService<IEnvelopeEncryptionProvider>();

        Assert.IsType<AzureKeyVaultEncryptionKeyProvider>(asKeyProvider);
        Assert.IsType<AzureKeyVaultEncryptionKeyProvider>(asEnvelopeProvider);
        Assert.Same(asKeyProvider, asEnvelopeProvider);
    }

    [Fact]
    public void ValidConfiguration_SelfRegistersSecureRandomGenerator_WhenNotAlreadyRegistered()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelAzureKeyVaultCryptography(ValidConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<CryptoRandomGenerator>(provider.GetRequiredService<ISecureRandomGenerator>());
    }

    [Fact]
    public void ExistingSecureRandomGeneratorRegistration_IsNeverOverridden()
    {
        var services = new ServiceCollection();
        var sentinel = new CryptoRandomGenerator();
        services.AddSingleton<ISecureRandomGenerator>(sentinel);

        services.AddSharedKernelAzureKeyVaultCryptography(ValidConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(sentinel, provider.GetRequiredService<ISecureRandomGenerator>());
    }

    [Fact]
    public async Task MissingVaultUri_ThrowsAtHostStartup()
    {
        var invalidConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Cryptography:KeyVault:Azure:CurrentKeyId"] = "primary",
                ["SharedKernel:Cryptography:KeyVault:Azure:KeyNames:primary"] = "tenant-data-key",
            })
            .Build();

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSharedKernelAzureKeyVaultCryptography(invalidConfig))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task EmptyKeyNames_ThrowsAtHostStartup()
    {
        var invalidConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Cryptography:KeyVault:Azure:VaultUri"] = "https://my-vault.vault.azure.net/",
                ["SharedKernel:Cryptography:KeyVault:Azure:CurrentKeyId"] = "primary",
            })
            .Build();

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSharedKernelAzureKeyVaultCryptography(invalidConfig))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task CurrentKeyIdNotInKeyNames_ThrowsAtHostStartup()
    {
        var invalidConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Cryptography:KeyVault:Azure:VaultUri"] = "https://my-vault.vault.azure.net/",
                ["SharedKernel:Cryptography:KeyVault:Azure:CurrentKeyId"] = "does-not-exist",
                ["SharedKernel:Cryptography:KeyVault:Azure:KeyNames:primary"] = "tenant-data-key",
            })
            .Build();

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSharedKernelAzureKeyVaultCryptography(invalidConfig))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task ValidConfiguration_StartsSuccessfully()
    {
        // KeyClient construction performs no network I/O — only calling one of its operations
        // does. A well-formed (but not necessarily reachable) VaultUri must start cleanly.
        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSharedKernelAzureKeyVaultCryptography(ValidConfiguration()))
            .Build();

        await host.StartAsync();
        await host.StopAsync();
    }
}
