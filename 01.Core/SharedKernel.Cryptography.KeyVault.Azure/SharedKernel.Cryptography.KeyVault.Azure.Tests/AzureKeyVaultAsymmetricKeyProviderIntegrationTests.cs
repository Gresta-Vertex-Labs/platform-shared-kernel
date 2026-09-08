using System.Security.Cryptography;
using SharedKernel.Cryptography.KeyVault.Azure.Options;
using SharedKernel.Cryptography.KeyVault.Azure.Tests.TestSupport;
using SharedKernel.Cryptography.Signing;
using Xunit;
using Xunit.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests;

/// <summary>
/// T-71 integration coverage against real Azure SDK network calls — mirrors
/// <c>AzureKeyVaultEncryptionKeyProviderIntegrationTests</c>'s (T-54/T-65) skip-if-unavailable
/// posture exactly.
/// </summary>
/// <remarks>
/// <para>
/// <b>What genuinely executes in this environment, and what does not — stated honestly rather
/// than implied.</b> This CI/dev sandbox has outbound HTTPS connectivity but no reachable Azure
/// Key Vault instance and no Azure credentials. That splits this file's coverage into two kinds:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Genuinely executed, every run:</b> the "unreachable vault surfaces as a thrown exception,
/// never a silent no-op" fail-closed requirement for both
/// <see cref="AzureKeyVaultAsymmetricKeyProvider.GetRsaKeyAsync"/> and
/// <see cref="AzureKeyVaultAsymmetricKeyProvider.GetEcdsaKeyAsync"/>, proven with a REAL
/// <see cref="Azure.Security.KeyVault.Keys.KeyClient"/> attempting a REAL TCP connection to a
/// loopback address nothing listens on (mirrors the exact technique the sibling encryption
/// provider's integration tests already use — a connection-refused failure, not a slow DNS
/// timeout). No mocking of any Azure SDK type is involved anywhere in this file.
/// </item>
/// <item>
/// <b>NOT exercised in this environment — genuinely untested here:</b> the full remote sign/verify
/// round trip — <see cref="RsaSignatureService.SignAsync"/>/<see cref="RsaSignatureService.VerifyAsync"/>
/// and <see cref="EcdsaSignatureService.SignAsync"/>/<see cref="EcdsaSignatureService.VerifyAsync"/>
/// driven entirely through <see cref="AzureKeyVaultAsymmetricKeyProvider"/>, never touching the
/// (internal, package-private) <c>KeyVaultRsaKey</c>/<c>KeyVaultEcdsaKey</c> wrapper types
/// directly — against a real RSA and a real EC (P-256) Azure Key Vault signing key. These tests
/// are written for real (no fakes) and
/// gated behind the <c>SHAREDKERNEL_TEST_AZURE_KEYVAULT_URI</c>/
/// <c>SHAREDKERNEL_TEST_AZURE_KEYVAULT_RSA_SIGNING_KEY_NAME</c>/
/// <c>SHAREDKERNEL_TEST_AZURE_KEYVAULT_ECDSA_SIGNING_KEY_NAME</c> environment variables
/// (authenticating via <see cref="Azure.Identity.DefaultAzureCredential"/>) — when unset, as they
/// are in this sandbox, each test reports a skip via <see cref="ITestOutputHelper"/> and returns
/// without asserting anything. Distinct environment variables from the encryption provider's own
/// integration tests (<c>SHAREDKERNEL_TEST_AZURE_KEYVAULT_KEY_NAME</c>) deliberately: a wrap/unwrap
/// data-encryption key and a sign/verify key are configured with different Key Vault key
/// operations and are not interchangeable. Do not read a green run of this file as proof the
/// happy-path remote sign/verify round trip works — it proves only the fail-closed behavior above
/// plus (in <c>AzureKeyVaultAsymmetricKeyProviderTests</c>) every locally-decidable validation
/// branch.
/// </item>
/// </list>
/// </remarks>
public sealed class AzureKeyVaultAsymmetricKeyProviderIntegrationTests(ITestOutputHelper output)
{
    // Loopback address, port 1 — reserved/never a listening service on any CI runner or dev
    // machine, and fails fast via connection-refused rather than a slow DNS-resolution timeout.
    private static readonly Uri UnreachableVaultUri = new("https://127.0.0.1:1/");

    private static AzureKeyVaultAsymmetricKeyProvider CreateUnreachableProvider() =>
        new(
            MsOptions.Create(new AzureKeyVaultCryptographyOptions
            {
                VaultUri = UnreachableVaultUri,
                CurrentKeyId = "primary",
                KeyNames = new Dictionary<string, string>
                {
                    ["primary"] = "tenant-data-key",
                    ["rsa-signing"] = "rsa-signing-key",
                    ["ecdsa-signing"] = "ecdsa-signing-key",
                },
                Credential = new FakeTokenCredential(),
            }));

    [Fact]
    public async Task GetRsaKeyAsync_UnreachableVault_ThrowsInsteadOfSilentNoOp()
    {
        AzureKeyVaultAsymmetricKeyProvider provider = CreateUnreachableProvider();

        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetRsaKeyAsync("rsa-signing").AsTask());
    }

    [Fact]
    public async Task GetEcdsaKeyAsync_UnreachableVault_ThrowsInsteadOfSilentNoOp()
    {
        AzureKeyVaultAsymmetricKeyProvider provider = CreateUnreachableProvider();

        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetEcdsaKeyAsync("ecdsa-signing").AsTask());
    }

    [Fact]
    public async Task RsaSignatureService_SignAsync_ThenVerifyAsync_RoundTripsAgainstRealAzureKeyVault()
    {
        (Uri VaultUri, string RsaKeyName, string EcdsaKeyName)? realVault = TryGetRealVaultConfiguration();
        if (realVault is null)
        {
            output.WriteLine(
                "SKIPPED: SHAREDKERNEL_TEST_AZURE_KEYVAULT_URI / " +
                "SHAREDKERNEL_TEST_AZURE_KEYVAULT_RSA_SIGNING_KEY_NAME are not set — no reachable " +
                "Azure Key Vault RSA signing key is configured for this test run. The remote " +
                "sign/verify round trip is NOT exercised in this environment; see this class's XML docs.");
            return;
        }

        AzureKeyVaultAsymmetricKeyProvider provider = CreateRealProvider(realVault.Value);
        var signatureService = new RsaSignatureService(provider);
        byte[] data = "P-494 RSA remote-signing round trip"u8.ToArray();

        byte[] signature = await signatureService.SignAsync(data, "rsa-signing");
        bool isValid = await signatureService.VerifyAsync(data, signature, "rsa-signing");

        Assert.True(isValid);

        // ExportParameters/ImportParameters must never release private key material, even against
        // a real vault-backed key.
        RSA rsa = await provider.GetRsaKeyAsync("rsa-signing");
        Assert.Throws<NotSupportedException>(() => rsa.ExportParameters(includePrivateParameters: false));
        Assert.Throws<NotSupportedException>(() => rsa.ImportParameters(default));
    }

    [Fact]
    public async Task EcdsaSignatureService_SignAsync_ThenVerifyAsync_RoundTripsAgainstRealAzureKeyVault()
    {
        (Uri VaultUri, string RsaKeyName, string EcdsaKeyName)? realVault = TryGetRealVaultConfiguration();
        if (realVault is null)
        {
            output.WriteLine(
                "SKIPPED: SHAREDKERNEL_TEST_AZURE_KEYVAULT_URI / " +
                "SHAREDKERNEL_TEST_AZURE_KEYVAULT_ECDSA_SIGNING_KEY_NAME are not set — no reachable " +
                "Azure Key Vault EC (P-256) signing key is configured for this test run. The remote " +
                "sign/verify round trip is NOT exercised in this environment; see this class's XML docs.");
            return;
        }

        AzureKeyVaultAsymmetricKeyProvider provider = CreateRealProvider(realVault.Value);
        var signatureService = new EcdsaSignatureService(provider);
        byte[] data = "P-494 ECDSA remote-signing round trip"u8.ToArray();

        byte[] signature = await signatureService.SignAsync(data, "ecdsa-signing");
        bool isValid = await signatureService.VerifyAsync(data, signature, "ecdsa-signing");

        Assert.True(isValid);

        ECDsa ecdsa = await provider.GetEcdsaKeyAsync("ecdsa-signing");
        Assert.Throws<NotSupportedException>(() => ecdsa.ExportParameters(includePrivateParameters: false));
        Assert.Throws<NotSupportedException>(() => ecdsa.ImportParameters(default));
        Assert.Throws<NotSupportedException>(() => ecdsa.GenerateKey(ECCurve.NamedCurves.nistP256));
    }

    private static AzureKeyVaultAsymmetricKeyProvider CreateRealProvider(
        (Uri VaultUri, string RsaKeyName, string EcdsaKeyName) realVault) =>
        new(
            MsOptions.Create(new AzureKeyVaultCryptographyOptions
            {
                VaultUri = realVault.VaultUri,
                CurrentKeyId = "rsa-signing",
                KeyNames = new Dictionary<string, string>
                {
                    ["rsa-signing"] = realVault.RsaKeyName,
                    ["ecdsa-signing"] = realVault.EcdsaKeyName,
                },
            }));

    private static (Uri VaultUri, string RsaKeyName, string EcdsaKeyName)? TryGetRealVaultConfiguration()
    {
        string? vaultUri = Environment.GetEnvironmentVariable("SHAREDKERNEL_TEST_AZURE_KEYVAULT_URI");
        string? rsaKeyName = Environment.GetEnvironmentVariable("SHAREDKERNEL_TEST_AZURE_KEYVAULT_RSA_SIGNING_KEY_NAME");
        string? ecdsaKeyName = Environment.GetEnvironmentVariable("SHAREDKERNEL_TEST_AZURE_KEYVAULT_ECDSA_SIGNING_KEY_NAME");

        if (string.IsNullOrWhiteSpace(vaultUri) ||
            string.IsNullOrWhiteSpace(rsaKeyName) ||
            string.IsNullOrWhiteSpace(ecdsaKeyName))
        {
            return null;
        }

        return (new Uri(vaultUri), rsaKeyName, ecdsaKeyName);
    }
}
