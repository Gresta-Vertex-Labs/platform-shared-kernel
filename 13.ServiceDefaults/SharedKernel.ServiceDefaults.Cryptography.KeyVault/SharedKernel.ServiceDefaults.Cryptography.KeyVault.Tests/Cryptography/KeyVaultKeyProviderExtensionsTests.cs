using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.KeyVault.Azure;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.ServiceDefaults.Cryptography;

namespace SharedKernel.ServiceDefaults.Cryptography.KeyVault.Tests.Cryptography;

public sealed class KeyVaultKeyProviderExtensionsTests
{
    /// <summary>
    /// A host builder pre-configured with a syntactically valid (never actually reached) Key Vault
    /// encryption section — needed for the tests that resolve <see cref="AzureKeyVaultEncryptionKeyProvider"/>,
    /// which reads its validated options but performs no network I/O on construction.
    /// </summary>
    private static HostApplicationBuilder CreateHostWithValidVaultOptions()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
        [
            new("SharedKernel:Cryptography:KeyVault:Azure:Encryption:VaultUri", "https://example.vault.azure.net/"),
            new("SharedKernel:Cryptography:KeyVault:Azure:Encryption:MasterKeyName", "test-kek"),
            new("SharedKernel:Cryptography:KeyVault:Azure:Encryption:DataKeySecretName", "test-data-keys"),
        ]);
        return builder;
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_NullBuilder_ThrowsArgumentNullException()
    {
        IHostApplicationBuilder builder = null!;

        var act = () => builder.AddSharedKernelKeyVaultKeyProvider();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_CalledOnce_RegistersEachProviderInterfaceExactlyOnce()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddSharedKernelKeyVaultKeyProvider();

        builder.Services.Count(d => d.ServiceType == typeof(AzureKeyVaultEncryptionKeyProvider)).Should().Be(1);
        builder.Services.Count(d => d.ServiceType == typeof(IEncryptionKeyProvider)).Should().Be(1);
        builder.Services.Count(d => d.ServiceType == typeof(IEnvelopeEncryptionProvider)).Should().Be(1);
        builder.Services.Count(d => d.ServiceType == typeof(IEncryptionKeyProviderProbe)).Should().Be(1);
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_CalledTwice_DoesNotDuplicateRegistration()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddSharedKernelKeyVaultKeyProvider();
        builder.AddSharedKernelKeyVaultKeyProvider();

        builder.Services.Count(d => d.ServiceType == typeof(AzureKeyVaultEncryptionKeyProvider)).Should().Be(1);
        builder.Services.Count(d => d.ServiceType == typeof(IEncryptionKeyProvider)).Should().Be(1);
        builder.Services.Count(d => d.ServiceType == typeof(IEnvelopeEncryptionProvider)).Should().Be(1);
        builder.Services.Count(d => d.ServiceType == typeof(IEncryptionKeyProviderProbe)).Should().Be(1);
        builder.Services.Count(d => d.ServiceType == typeof(ISecureRandomGenerator)).Should().Be(1);
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_ReturnsCryptographyBuilderOverTheHostServices()
    {
        var builder = Host.CreateApplicationBuilder();

        var result = builder.AddSharedKernelKeyVaultKeyProvider();

        result.Services.Should().BeSameAs(builder.Services);
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_RegistersNoCachingWrapper()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddSharedKernelKeyVaultKeyProvider();

        builder.Services.Should().NotContain(d => d.ServiceType == typeof(CachedEncryptionKeyProvider));
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_EveryProviderInterface_ResolvesTheSameRawSingleton()
    {
        var builder = CreateHostWithValidVaultOptions();

        builder.AddSharedKernelKeyVaultKeyProvider();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        AzureKeyVaultEncryptionKeyProvider raw = provider.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>();

        provider.GetRequiredService<IEncryptionKeyProvider>().Should().BeSameAs(raw);
        provider.GetRequiredService<IEnvelopeEncryptionProvider>().Should().BeSameAs(raw);
        provider.GetRequiredService<IEncryptionKeyProviderProbe>().Should().BeSameAs(raw);
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_ChainedSymmetricEncryption_ResolvesAgainstTheProvider()
    {
        var builder = CreateHostWithValidVaultOptions();

        builder.AddSharedKernelKeyVaultKeyProvider().AddSymmetricEncryption();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<ISymmetricEncryptionService>().Should().NotBeNull();
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_RegistersNoSynchronousProvider()
    {
        var builder = CreateHostWithValidVaultOptions();

        builder.AddSharedKernelKeyVaultKeyProvider();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        provider.GetService<ISynchronousEncryptionKeyProvider>().Should().BeNull();
    }
}
