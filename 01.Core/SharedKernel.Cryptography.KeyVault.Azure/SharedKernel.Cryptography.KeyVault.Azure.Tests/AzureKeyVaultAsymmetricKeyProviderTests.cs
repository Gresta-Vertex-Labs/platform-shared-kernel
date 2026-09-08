using SharedKernel.Cryptography.KeyVault.Azure.Options;
using SharedKernel.Cryptography.Signing;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests;

/// <summary>
/// Local-only coverage for <see cref="AzureKeyVaultAsymmetricKeyProvider"/> — every case here
/// exercises a code path that returns before any Azure SDK call is made (purely local
/// validation), so it needs no reachable vault or credentials. Genuine network-round-trip
/// behavior (an unreachable vault surfacing as a thrown exception, and the full remote sign/verify
/// round trip through <see cref="RsaSignatureService"/>/<see cref="EcdsaSignatureService"/> against
/// a real Azure Key Vault key) is covered separately in
/// <c>AzureKeyVaultAsymmetricKeyProviderIntegrationTests</c> — see that file's own docs for what is
/// and is not genuinely exercised in this environment.
/// </summary>
public sealed class AzureKeyVaultAsymmetricKeyProviderTests
{
    private static AzureKeyVaultAsymmetricKeyProvider CreateProvider() =>
        new(
            MsOptions.Create(new AzureKeyVaultCryptographyOptions
            {
                VaultUri = new Uri("https://my-vault.vault.azure.net/"),
                CurrentKeyId = "primary",
                KeyNames = new Dictionary<string, string>
                {
                    ["primary"] = "tenant-data-key",
                    ["rsa-signing"] = "rsa-signing-key",
                    ["ecdsa-signing"] = "ecdsa-signing-key",
                },
            }));

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new AzureKeyVaultAsymmetricKeyProvider(null!));
    }

    [Fact]
    public void Constructor_NoExplicitCredential_DefaultsToDefaultAzureCredential_WithoutThrowing()
    {
        // DefaultAzureCredential construction performs no network I/O by itself.
        var exception = Record.Exception(CreateProvider);

        Assert.Null(exception);
    }

    [Fact]
    public void NeverImplementsISynchronousAsymmetricKeyProvider()
    {
        // AzureKeyVaultAsymmetricKeyProvider always performs a genuine network round trip — it
        // must never claim otherwise via this opt-in marker (P-493/P-494's central safety
        // invariant: an unmarked provider makes IAsymmetricSignatureService's sync Sign/Verify
        // throw NotSupportedException instead of silently blocking a thread on real I/O).
        AzureKeyVaultAsymmetricKeyProvider provider = CreateProvider();

        Assert.IsNotAssignableFrom<ISynchronousAsymmetricKeyProvider>(provider);
        Assert.False(AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous(provider));
    }

    [Fact]
    public async Task GetRsaKeyAsync_NullKeyId_Throws()
    {
        AzureKeyVaultAsymmetricKeyProvider provider = CreateProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GetRsaKeyAsync(null!).AsTask());
    }

    [Fact]
    public async Task GetEcdsaKeyAsync_NullKeyId_Throws()
    {
        AzureKeyVaultAsymmetricKeyProvider provider = CreateProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GetEcdsaKeyAsync(null!).AsTask());
    }

    [Fact]
    public async Task GetRsaKeyAsync_UnknownKeyId_ThrowsKeyNotFoundException_WithoutCallingAzure()
    {
        // Resolving keyId -> Azure key name is a purely local dictionary lookup — an unrecognized
        // keyId is rejected before any Azure SDK call is ever attempted, per
        // IAsymmetricKeyProvider's documented KeyNotFoundException contract.
        AzureKeyVaultAsymmetricKeyProvider provider = CreateProvider();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => provider.GetRsaKeyAsync("does-not-exist").AsTask());
    }

    [Fact]
    public async Task GetEcdsaKeyAsync_UnknownKeyId_ThrowsKeyNotFoundException_WithoutCallingAzure()
    {
        AzureKeyVaultAsymmetricKeyProvider provider = CreateProvider();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => provider.GetEcdsaKeyAsync("does-not-exist").AsTask());
    }

    [Fact]
    public void RsaSignatureService_ConstructsWithAzureProvider_NoCodeChangesNeeded()
    {
        // Compile-time proof (D-75/D-76's own stated acceptance criterion): RsaSignatureService
        // (P-493) consumes AzureKeyVaultAsymmetricKeyProvider through the exact same
        // IAsymmetricKeyProvider contract as any other implementation — zero changes to
        // RsaSignatureService's own source were needed for this phase.
        AzureKeyVaultAsymmetricKeyProvider provider = CreateProvider();

        var exception = Record.Exception(() => _ = new RsaSignatureService(provider));

        Assert.Null(exception);
    }

    [Fact]
    public void EcdsaSignatureService_ConstructsWithAzureProvider_NoCodeChangesNeeded()
    {
        AzureKeyVaultAsymmetricKeyProvider provider = CreateProvider();

        var exception = Record.Exception(() => _ = new EcdsaSignatureService(provider));

        Assert.Null(exception);
    }

    [Fact]
    public void RsaSignatureService_Sign_AgainstAzureProvider_ThrowsNotSupported_RatherThanBlockingAThread()
    {
        // AzureKeyVaultAsymmetricKeyProvider never implements ISynchronousAsymmetricKeyProvider,
        // so the retained synchronous Sign/Verify members must refuse to bridge onto it — this is
        // decided at RsaSignatureService construction time and needs no network to prove.
        var service = new RsaSignatureService(CreateProvider());

        Assert.Throws<NotSupportedException>(() => service.Sign([1, 2, 3], "rsa-signing"));
    }

    [Fact]
    public void EcdsaSignatureService_Sign_AgainstAzureProvider_ThrowsNotSupported_RatherThanBlockingAThread()
    {
        var service = new EcdsaSignatureService(CreateProvider());

        Assert.Throws<NotSupportedException>(() => service.Sign([1, 2, 3], "ecdsa-signing"));
    }
}
