using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Cryptography.KeyVault.Azure;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.ServiceDefaults.Cryptography;

namespace SharedKernel.ServiceDefaults.Cryptography.KeyVault.Tests.Cryptography;

/// <summary>
/// Covers WO-068/P-449's <c>AddSharedKernelKeyVaultKeyProvider</c> registration half plus
/// WO-081/P-503's <c>cacheTtl</c> caching-wrap addition (C-70).
/// </summary>
public sealed class KeyVaultKeyProviderExtensionsTests
{
    /// <summary>
    /// A host builder pre-configured with a syntactically valid (never actually reached)
    /// <c>AzureKeyVaultCryptographyOptions</c> section — needed only for the tests below that
    /// actually resolve <see cref="AzureKeyVaultEncryptionKeyProvider"/> from a built
    /// <see cref="ServiceProvider"/>, since its constructor eagerly reads <c>IOptions&lt;T&gt;.Value</c>
    /// (Data Annotations validation) but performs no network I/O of its own.
    /// </summary>
    private static HostApplicationBuilder CreateHostWithValidVaultOptions()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
        [
            new("SharedKernel:Cryptography:KeyVault:Azure:VaultUri", "https://example.vault.azure.net/"),
            new("SharedKernel:Cryptography:KeyVault:Azure:CurrentKeyId", "default"),
            new("SharedKernel:Cryptography:KeyVault:Azure:KeyNames:default", "test-key"),
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
    public void AddSharedKernelKeyVaultKeyProvider_CalledOnce_RegistersProviderExactlyOnce()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddSharedKernelKeyVaultKeyProvider();

        builder.Services.Count(d => d.ServiceType == typeof(AzureKeyVaultEncryptionKeyProvider)).Should().Be(1);
        builder.Services.Count(d => d.ServiceType == typeof(IEnvelopeEncryptionProvider)).Should().Be(1);

        // Two IEncryptionKeyProvider registrations by design under the default cacheTtl (D-38): the
        // raw registration from 01.Core's call-through, plus this method's own re-registration
        // redirecting to the CachedEncryptionKeyProvider wrapper. MS.DI resolves the LAST
        // registration for a single-instance request — see the *ResolvesEncryptionKeyProviderAs*
        // tests below for the resolution-level proof.
        builder.Services.Count(d => d.ServiceType == typeof(IEncryptionKeyProvider)).Should().Be(2);
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_CalledTwice_DoesNotDuplicateRegistration()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddSharedKernelKeyVaultKeyProvider();
        builder.AddSharedKernelKeyVaultKeyProvider();

        builder.Services.Count(d => d.ServiceType == typeof(AzureKeyVaultEncryptionKeyProvider)).Should().Be(1);
        builder.Services.Count(d => d.ServiceType == typeof(IEncryptionKeyProvider)).Should().Be(2);
        builder.Services.Count(d => d.ServiceType == typeof(IEnvelopeEncryptionProvider)).Should().Be(1);
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_ReturnsSameBuilderInstance_ForFluentChaining()
    {
        var builder = Host.CreateApplicationBuilder();

        var result = builder.AddSharedKernelKeyVaultKeyProvider();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_DefaultCall_ResolvesEncryptionKeyProviderAsCachedWrapper()
    {
        var builder = CreateHostWithValidVaultOptions();

        builder.AddSharedKernelKeyVaultKeyProvider();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        IEncryptionKeyProvider resolved = provider.GetRequiredService<IEncryptionKeyProvider>();

        resolved.Should().BeOfType<CachedEncryptionKeyProvider>();
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_DefaultCall_EnvelopeProviderStaysReferenceEqualToRawSingleton()
    {
        var builder = CreateHostWithValidVaultOptions();

        builder.AddSharedKernelKeyVaultKeyProvider();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        AzureKeyVaultEncryptionKeyProvider raw = provider.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>();
        IEnvelopeEncryptionProvider envelope = provider.GetRequiredService<IEnvelopeEncryptionProvider>();

        // Never cache-masked — a real per-call vault operation, not a cacheable key lookup.
        envelope.Should().BeSameAs(raw);
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_DefaultCall_ProbeStaysReferenceEqualToRawSingleton()
    {
        var builder = CreateHostWithValidVaultOptions();

        builder.AddSharedKernelKeyVaultKeyProvider();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        AzureKeyVaultEncryptionKeyProvider raw = provider.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>();
        IEncryptionKeyProviderProbe probe = provider.GetRequiredService<IEncryptionKeyProviderProbe>();

        // A readiness probe must always observe live KMS state — never a cached signal.
        probe.Should().BeSameAs(raw);
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_CacheTtlZero_DisablesWrapping()
    {
        var builder = CreateHostWithValidVaultOptions();

        builder.AddSharedKernelKeyVaultKeyProvider(TimeSpan.Zero);

        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        AzureKeyVaultEncryptionKeyProvider raw = provider.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>();
        IEncryptionKeyProvider resolved = provider.GetRequiredService<IEncryptionKeyProvider>();

        resolved.Should().BeSameAs(raw);
        builder.Services.Count(d => d.ServiceType == typeof(CachedEncryptionKeyProvider)).Should().Be(0);
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_NegativeCacheTtl_ThrowsArgumentOutOfRangeException()
    {
        var builder = Host.CreateApplicationBuilder();

        var act = () => builder.AddSharedKernelKeyVaultKeyProvider(TimeSpan.FromSeconds(-1));

        act.Should().Throw<ArgumentOutOfRangeException>();
        builder.Services.Any(d => d.ServiceType == typeof(AzureKeyVaultEncryptionKeyProvider)).Should().BeFalse();
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_DefaultCall_CachedEncryptionKeyProviderIndependentlyResolvable()
    {
        var builder = CreateHostWithValidVaultOptions();

        builder.AddSharedKernelKeyVaultKeyProvider();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        // Proves 06.Persistence's .WithExternalEncryptionKeyProvider<CachedEncryptionKeyProvider>()
        // compatibility without invoking 06.Persistence itself — no ProjectReference to it exists.
        CachedEncryptionKeyProvider cached = provider.GetRequiredService<CachedEncryptionKeyProvider>();
        IEncryptionKeyProvider resolvedAsInterface = provider.GetRequiredService<IEncryptionKeyProvider>();

        resolvedAsInterface.Should().BeSameAs(cached);
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_CalledTwice_DoesNotDoubleRegisterCachedEncryptionKeyProvider()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddSharedKernelKeyVaultKeyProvider();
        builder.AddSharedKernelKeyVaultKeyProvider();

        builder.Services.Count(d => d.ServiceType == typeof(CachedEncryptionKeyProvider)).Should().Be(1);
        builder.Services.Count(d => d.ServiceType == typeof(IEncryptionKeyProvider)).Should().Be(2);
    }
}
