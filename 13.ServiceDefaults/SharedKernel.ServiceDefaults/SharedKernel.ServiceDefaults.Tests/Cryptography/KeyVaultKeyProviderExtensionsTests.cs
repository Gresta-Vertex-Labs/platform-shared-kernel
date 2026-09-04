using FluentAssertions;
using Microsoft.Extensions.Hosting;
using SharedKernel.Cryptography.KeyVault.Azure;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.ServiceDefaults.Cryptography;

namespace SharedKernel.ServiceDefaults.Tests.Cryptography;

/// <summary>
/// Covers WO-068/P-449's <c>AddSharedKernelKeyVaultKeyProvider</c> registration half — the
/// readiness-probe half remains blocked (D-33) and is out of scope here.
/// </summary>
public sealed class KeyVaultKeyProviderExtensionsTests
{
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
        builder.Services.Count(d => d.ServiceType == typeof(IEncryptionKeyProvider)).Should().Be(1);
        builder.Services.Count(d => d.ServiceType == typeof(IEnvelopeEncryptionProvider)).Should().Be(1);
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
    }

    [Fact]
    public void AddSharedKernelKeyVaultKeyProvider_ReturnsSameBuilderInstance_ForFluentChaining()
    {
        var builder = Host.CreateApplicationBuilder();

        var result = builder.AddSharedKernelKeyVaultKeyProvider();

        result.Should().BeSameAs(builder);
    }
}
