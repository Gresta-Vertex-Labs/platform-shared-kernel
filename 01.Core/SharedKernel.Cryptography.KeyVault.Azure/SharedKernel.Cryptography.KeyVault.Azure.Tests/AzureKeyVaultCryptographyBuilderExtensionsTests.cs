using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.KeyVault.Azure.Tests.Fakes;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using Xunit;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests;

public sealed class AzureKeyVaultCryptographyBuilderExtensionsTests
{
    [Fact]
    public void AddAzureKeyVaultEncryption_NullArguments_Throw()
    {
        IConfiguration configuration = Configuration(AzureKeyVaultOptionsValidationTests.EncryptionSettings());
        ICryptographyBuilder builder = new ServiceCollection().AddSharedKernelCryptography(configuration);

        Assert.Throws<ArgumentNullException>(() => AzureKeyVaultCryptographyBuilderExtensions.AddAzureKeyVaultEncryption(null!, configuration));
        Assert.Throws<ArgumentNullException>(() => builder.AddAzureKeyVaultEncryption(null!));
    }

    [Fact]
    public void AddAzureKeyVaultSigning_NullArguments_Throw()
    {
        IConfiguration configuration = Configuration(AzureKeyVaultOptionsValidationTests.SigningSettings());
        ICryptographyBuilder builder = new ServiceCollection().AddSharedKernelCryptography(configuration);

        Assert.Throws<ArgumentNullException>(() => AzureKeyVaultCryptographyBuilderExtensions.AddAzureKeyVaultSigning(null!, configuration));
        Assert.Throws<ArgumentNullException>(() => builder.AddAzureKeyVaultSigning(null!));
    }

    [Fact]
    public void AddAzureKeyVaultEncryption_Always_ReturnsSameBuilder()
    {
        IConfiguration configuration = Configuration(AzureKeyVaultOptionsValidationTests.EncryptionSettings());
        ICryptographyBuilder builder = new ServiceCollection().AddSharedKernelCryptography(configuration);

        Assert.Same(builder, builder.AddAzureKeyVaultEncryption(configuration));
        Assert.Same(builder, builder.AddAzureKeyVaultSigning(configuration));
    }

    [Fact]
    public void AddAzureKeyVaultEncryption_Resolved_AllContractsShareOneProvider()
    {
        IConfiguration configuration = Configuration(AzureKeyVaultOptionsValidationTests.EncryptionSettings());
        var credential = new FakeTokenCredential();
        var services = new ServiceCollection();
        services.AddSingleton<TokenCredential>(credential);
        services.AddSharedKernelCryptography(configuration).AddAzureKeyVaultEncryption(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        AzureKeyVaultEncryptionKeyProvider concrete = provider.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>();

        Assert.Same(concrete, provider.GetRequiredService<IEncryptionKeyProvider>());
        Assert.Same(concrete, provider.GetRequiredService<IEnvelopeEncryptionProvider>());
        Assert.Same(concrete, provider.GetRequiredService<IEncryptionKeyProviderProbe>());
        Assert.Same(credential, provider.GetRequiredService<TokenCredential>());
        Assert.Equal(0, credential.Calls);
    }

    [Fact]
    public void AddAzureKeyVaultEncryption_WithServiceRegistrations_ResolvesEncryptionServices()
    {
        IConfiguration configuration = Configuration(AzureKeyVaultOptionsValidationTests.EncryptionSettings());
        var services = new ServiceCollection();
        services.AddSingleton<TokenCredential>(new FakeTokenCredential());
        services.AddSharedKernelCryptography(configuration)
            .AddAzureKeyVaultEncryption(configuration)
            .AddSymmetricEncryption()
            .AddEnvelopeEncryption();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<AesGcmEncryptionService>(provider.GetRequiredService<ISymmetricEncryptionService>());
        Assert.IsType<EnvelopeEncryptionService>(provider.GetRequiredService<IEnvelopeEncryptionService>());
    }

    [Fact]
    public void AddAzureKeyVaultEncryption_Always_RegistersSingletons()
    {
        IServiceCollection services = RegisterBoth(new ServiceCollection());

        foreach (Type type in new[]
        {
            typeof(AzureKeyVaultEncryptionKeyProvider),
            typeof(IEncryptionKeyProvider),
            typeof(IEnvelopeEncryptionProvider),
            typeof(IEncryptionKeyProviderProbe),
            typeof(AzureKeyVaultSigningKeyProvider),
            typeof(ISigningKeyProvider),
            typeof(TokenCredential),
            typeof(TimeProvider),
            typeof(ISecureRandomGenerator),
        })
        {
            ServiceDescriptor descriptor = Assert.Single(services, d => d.ServiceType == type);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }
    }

    [Fact]
    public void AddAzureKeyVaultEncryption_NoCredentialRegistered_RegistersDefaultCredentialFactory()
    {
        IConfiguration configuration = Configuration(AzureKeyVaultOptionsValidationTests.EncryptionSettings());
        var services = new ServiceCollection();

        services.AddSharedKernelCryptography(configuration).AddAzureKeyVaultEncryption(configuration);

        ServiceDescriptor descriptor = Assert.Single(services, d => d.ServiceType == typeof(TokenCredential));
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void AddAzureKeyVaultEncryption_CredentialRegisteredFirst_KeepsIt()
    {
        var credential = new FakeTokenCredential();
        var services = new ServiceCollection();
        services.AddSingleton<TokenCredential>(credential);

        RegisterBoth(services);

        ServiceDescriptor descriptor = Assert.Single(services, d => d.ServiceType == typeof(TokenCredential));
        Assert.Same(credential, descriptor.ImplementationInstance);
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Same(credential, provider.GetRequiredService<TokenCredential>());
    }

    [Fact]
    public void AddAzureKeyVaultEncryption_NoTimeProviderRegistered_UsesSystemTimeProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TokenCredential>(new FakeTokenCredential());
        RegisterBoth(services);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void AddAzureKeyVaultEncryption_TimeProviderRegisteredFirst_KeepsIt()
    {
        var time = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<TokenCredential>(new FakeTokenCredential());

        RegisterBoth(services);

        Assert.Single(services, d => d.ServiceType == typeof(TimeProvider));
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Same(time, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void AddAzureKeyVaultEncryption_CalledTwice_DoesNotDuplicateRegistrations()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton<TokenCredential>(new FakeTokenCredential());
        RegisterBoth(services);
        int[] before = CountDescriptors(services);

        RegisterBoth(services);

        Assert.Equal(before, CountDescriptors(services));
        Assert.All(before, count => Assert.Equal(1, count));
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<IEncryptionKeyProvider>());
        Assert.Single(provider.GetServices<ISigningKeyProvider>());
    }

    [Fact]
    public void AddAzureKeyVaultSigning_Resolved_RegistersSigningKeyProvider()
    {
        IConfiguration configuration = Configuration(AzureKeyVaultOptionsValidationTests.SigningSettings());
        var services = new ServiceCollection();
        services.AddSingleton<TokenCredential>(new FakeTokenCredential());
        services.AddSharedKernelCryptography(configuration).AddAzureKeyVaultSigning(configuration).AddAsymmetricSigning();
        using ServiceProvider provider = services.BuildServiceProvider();

        AzureKeyVaultSigningKeyProvider concrete = provider.GetRequiredService<AzureKeyVaultSigningKeyProvider>();

        Assert.Same(concrete, provider.GetRequiredService<ISigningKeyProvider>());
        Assert.IsType<AsymmetricSignatureService>(provider.GetRequiredService<IAsymmetricSignatureService>());
        Assert.Null(provider.GetService<IEncryptionKeyProvider>());
    }

    [Fact]
    public void AddAzureKeyVaultEncryption_ProviderRegisteredFirst_KeepsExistingProvider()
    {
        IConfiguration configuration = Configuration(AzureKeyVaultOptionsValidationTests.EncryptionSettings());
        var existing = new StaticEncryptionKeyProvider("k1", [new CryptographicKey("k1", new byte[32])]);
        var services = new ServiceCollection();
        services.AddSingleton<IEncryptionKeyProvider>(existing);
        services.AddSingleton<TokenCredential>(new FakeTokenCredential());

        services.AddSharedKernelCryptography(configuration).AddAzureKeyVaultEncryption(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Same(existing, provider.GetRequiredService<IEncryptionKeyProvider>());
    }

    private static IServiceCollection RegisterBoth(IServiceCollection services)
    {
        Dictionary<string, string?> settings = AzureKeyVaultOptionsValidationTests.EncryptionSettings();
        foreach ((string key, string? value) in AzureKeyVaultOptionsValidationTests.SigningSettings())
        {
            settings[key] = value;
        }

        IConfiguration configuration = Configuration(settings);
        services.AddSharedKernelCryptography(configuration)
            .AddAzureKeyVaultEncryption(configuration)
            .AddAzureKeyVaultSigning(configuration);
        return services;
    }

    private static int[] CountDescriptors(IServiceCollection services) =>
    [
        .. new[]
        {
            typeof(AzureKeyVaultEncryptionKeyProvider),
            typeof(IEncryptionKeyProvider),
            typeof(IEnvelopeEncryptionProvider),
            typeof(IEncryptionKeyProviderProbe),
            typeof(AzureKeyVaultSigningKeyProvider),
            typeof(ISigningKeyProvider),
            typeof(TokenCredential),
            typeof(TimeProvider),
            typeof(ISecureRandomGenerator),
        }.Select(type => services.Count(d => d.ServiceType == type)),
    ];

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
