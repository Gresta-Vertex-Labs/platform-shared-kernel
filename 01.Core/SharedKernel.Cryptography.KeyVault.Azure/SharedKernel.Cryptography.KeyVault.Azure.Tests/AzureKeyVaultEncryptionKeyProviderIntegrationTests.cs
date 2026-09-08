using SharedKernel.Cryptography.KeyVault.Azure.Options;
using SharedKernel.Cryptography.KeyVault.Azure.Tests.TestSupport;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Results;
using Xunit;
using Xunit.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests;

/// <summary>
/// T-54/T-65/T-74 integration coverage against real Azure SDK network calls.
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
/// never a silent no-op" fail-closed requirement, proven with a REAL <see cref="Azure.Security.KeyVault.Keys.KeyClient"/>/
/// <see cref="Azure.Security.KeyVault.Keys.Cryptography.CryptographyClient"/> attempting a REAL
/// TCP connection to a loopback address nothing listens on (mirrors the exact technique
/// <c>13.ServiceDefaults</c>'s <c>KeyVaultConfigurationExtensionsTests</c> already uses for the
/// same class of "unreachable dependency" proof — a connection-refused failure, not a slow DNS
/// timeout). No mocking of any Azure SDK type is involved anywhere in this file. Also genuinely
/// executed: <see cref="AzureKeyVaultEncryptionKeyProvider.ProbeAsync"/>'s <em>opposite</em>
/// contract against the same unreachable vault — it must catch the identical connection failure
/// and report <see cref="EncryptionKeyProviderHealth.IsHealthy"/> <see langword="false"/> rather
/// than letting it propagate, proving the one deliberate carve-out from this class's fail-closed
/// rule actually holds; and (P-496/WO-081) <see cref="AzureKeyVaultEncryptionKeyProvider.MintNewVersionAsync"/>'s
/// identical fail-closed contract against the same unreachable vault.
/// </item>
/// <item>
/// <b>NOT exercised in this environment — genuinely untested here:</b> the successful
/// <c>GenerateDataKeyAsync</c> → <c>UnwrapDataKeyAsync</c> round trip, the
/// <c>GetCurrentKeyAsync</c>/<c>GetKeyAsync</c> direct-retrieval round trip built atop it, and
/// (P-496/WO-081) the full <c>MintNewVersionAsync</c> → encrypt-under-new-current →
/// decrypt-under-old-tag durable-version-registry lifecycle, all against a real Azure Key Vault
/// key. These tests are written for real (no fakes) and gated behind the
/// <c>SHAREDKERNEL_TEST_AZURE_KEYVAULT_URI</c>/<c>SHAREDKERNEL_TEST_AZURE_KEYVAULT_KEY_NAME</c>
/// environment variables (authenticating via <see cref="Azure.Identity.DefaultAzureCredential"/>)
/// — when unset, as they are in this sandbox, the test reports a skip via
/// <see cref="ITestOutputHelper"/> and returns without asserting anything. Do not read a green
/// run of this file as proof the happy-path wrap/unwrap round trip or the durable version registry
/// works — that (call-counting-double-backed, not live-network) proof is
/// <c>AzureKeyVaultEncryptionKeyProviderVersionRegistryTests</c>'s job (T-73); this file's green
/// run proves only the fail-closed behavior above plus (in
/// <c>AzureKeyVaultEncryptionKeyProviderTests</c>) every locally-decidable validation branch.
/// </item>
/// </list>
/// </remarks>
public sealed class AzureKeyVaultEncryptionKeyProviderIntegrationTests(ITestOutputHelper output)
{
    // Loopback address, port 1 — reserved/never a listening service on any CI runner or dev
    // machine, and fails fast via connection-refused rather than a slow DNS-resolution timeout.
    private static readonly Uri UnreachableVaultUri = new("https://127.0.0.1:1/");

    private static AzureKeyVaultEncryptionKeyProvider CreateUnreachableProvider() =>
        new(
            MsOptions.Create(new AzureKeyVaultCryptographyOptions
            {
                VaultUri = UnreachableVaultUri,
                CurrentKeyId = "primary",
                KeyNames = new Dictionary<string, string> { ["primary"] = "tenant-data-key" },
                Credential = new FakeTokenCredential(),
            }),
            new CryptoRandomGenerator());

    [Fact]
    public async Task GetCurrentKeyAsync_UnreachableVault_ThrowsInsteadOfSilentNoOp()
    {
        AzureKeyVaultEncryptionKeyProvider provider = CreateUnreachableProvider();

        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetCurrentKeyAsync().AsTask());
    }

    [Fact]
    public async Task GenerateDataKeyAsync_UnreachableVault_ThrowsInsteadOfSilentNoOp()
    {
        AzureKeyVaultEncryptionKeyProvider provider = CreateUnreachableProvider();

        await Assert.ThrowsAnyAsync<Exception>(() => provider.GenerateDataKeyAsync().AsTask());
    }

    [Fact]
    public async Task ProbeAsync_UnreachableVault_ReturnsUnhealthy_RatherThanThrowing()
    {
        // Unlike every other member of this class, ProbeAsync must never let the Azure SDK
        // exception propagate — it is the one deliberate, narrow exception to this class's
        // otherwise-universal fail-closed-via-exception contract. See ProbeAsync's own XML docs.
        AzureKeyVaultEncryptionKeyProvider provider = CreateUnreachableProvider();

        EncryptionKeyProviderHealth health = await provider.ProbeAsync();

        Assert.False(health.IsHealthy);
        Assert.False(string.IsNullOrWhiteSpace(health.Description));
    }

    [Fact]
    public async Task UnwrapDataKeyAsync_WellFormedMasterKeyId_UnreachableVault_ThrowsRatherThanReturningResultFailure()
    {
        // A syntactically well-formed masterKeyId (passes the local check) pointed at the same
        // unreachable vault — proves the real Azure SDK call beyond the local check also fails
        // closed via a thrown exception, never silently downgraded to a Result failure.
        AzureKeyVaultEncryptionKeyProvider provider = CreateUnreachableProvider();
        string wellFormedButUnreachable = $"{UnreachableVaultUri}keys/tenant-data-key/abc123";

        await Assert.ThrowsAnyAsync<Exception>(
            () => provider.UnwrapDataKeyAsync([1, 2, 3, 4], wellFormedButUnreachable).AsTask());
    }

    [Fact]
    public async Task MintNewVersionAsync_UnreachableVault_ThrowsInsteadOfSilentNoOp()
    {
        // P-496/WO-081: MintNewVersionAsync must fail closed exactly like every other member of
        // this class — an unreachable vault must never silently produce a "minted" version.
        AzureKeyVaultEncryptionKeyProvider provider = CreateUnreachableProvider();

        await Assert.ThrowsAnyAsync<Exception>(() => provider.MintNewVersionAsync().AsTask());
    }

    [Fact]
    public async Task GenerateDataKeyAsync_ThenUnwrapDataKeyAsync_RoundTripsAgainstRealAzureKeyVault()
    {
        (Uri VaultUri, string KeyName)? realVault = TryGetRealVaultConfiguration();
        if (realVault is null)
        {
            output.WriteLine(
                "SKIPPED: SHAREDKERNEL_TEST_AZURE_KEYVAULT_URI / SHAREDKERNEL_TEST_AZURE_KEYVAULT_KEY_NAME " +
                "are not set — no reachable Azure Key Vault is configured for this test run. " +
                "The generate/wrap -> unwrap round trip against a real vault is NOT exercised in this " +
                "environment; see this class's XML docs.");
            return;
        }

        var provider = new AzureKeyVaultEncryptionKeyProvider(
            MsOptions.Create(new AzureKeyVaultCryptographyOptions
            {
                VaultUri = realVault.Value.VaultUri,
                CurrentKeyId = "primary",
                KeyNames = new Dictionary<string, string> { ["primary"] = realVault.Value.KeyName },
            }),
            new CryptoRandomGenerator());

        EnvelopeDataKey dataKey = await provider.GenerateDataKeyAsync();
        Result<byte[]> unwrapped = await provider.UnwrapDataKeyAsync(dataKey.WrappedKey, dataKey.MasterKeyId);

        Assert.True(unwrapped.IsSuccess);
        Assert.Equal(dataKey.PlaintextKey, unwrapped.Value);

        CryptographicKey current = await provider.GetCurrentKeyAsync();
        var retrieved = await provider.GetKeyAsync(current.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(current.Material, retrieved!.Material);

        EncryptionKeyProviderHealth health = await provider.ProbeAsync();
        Assert.True(health.IsHealthy);
        Assert.Null(health.Description);
    }

    [Fact]
    public async Task MintNewVersionAsync_ThenEncryptUnderNewCurrent_ThenDecryptUnderOldTag_RoundTripsAgainstRealAzureKeyVault()
    {
        // T-74 (P-496/WO-081): the full mint -> encrypt-under-new-current -> decrypt-under-old-tag
        // lifecycle against a real (or env-gated dev-tenant) vault — mirrors T-54/T-65/T-71's
        // existing skip-if-unavailable-gated real-vault posture rather than inventing a new
        // harness.
        (Uri VaultUri, string KeyName)? realVault = TryGetRealVaultConfiguration();
        if (realVault is null)
        {
            output.WriteLine(
                "SKIPPED: SHAREDKERNEL_TEST_AZURE_KEYVAULT_URI / SHAREDKERNEL_TEST_AZURE_KEYVAULT_KEY_NAME " +
                "are not set — no reachable Azure Key Vault is configured for this test run. " +
                "The MintNewVersionAsync/durable-version-registry round trip against a real vault " +
                "is NOT exercised in this environment; see this class's XML docs.");
            return;
        }

        var provider = new AzureKeyVaultEncryptionKeyProvider(
            MsOptions.Create(new AzureKeyVaultCryptographyOptions
            {
                VaultUri = realVault.Value.VaultUri,
                CurrentKeyId = "primary",
                KeyNames = new Dictionary<string, string> { ["primary"] = realVault.Value.KeyName },
            }),
            new CryptoRandomGenerator());

        string firstTag = await provider.MintNewVersionAsync();
        CryptographicKey firstCurrent = await provider.GetCurrentKeyAsync();
        Assert.Equal(firstTag, firstCurrent.Id);

        // Compact, durable identifiers (D-81/D-83): well under the previous ~470-byte
        // self-decodable envelope shape.
        Assert.True(firstCurrent.Id.Length < 32, $"Expected a short version tag, got '{firstCurrent.Id}' ({firstCurrent.Id.Length} chars).");

        string secondTag = await provider.MintNewVersionAsync();
        CryptographicKey secondCurrent = await provider.GetCurrentKeyAsync();
        Assert.Equal(secondTag, secondCurrent.Id);
        Assert.NotEqual(firstTag, secondTag);
        Assert.True(secondCurrent.Id.Length < 32, $"Expected a short version tag, got '{secondCurrent.Id}' ({secondCurrent.Id.Length} chars).");

        // The now-superseded first tag must still decrypt.
        CryptographicKey? resolvedOldTag = await provider.GetKeyAsync(firstTag);
        Assert.NotNull(resolvedOldTag);
        Assert.Equal(firstCurrent.Material, resolvedOldTag!.Material);

        // A second replica-simulating provider instance must converge on the same current tag.
        var secondProvider = new AzureKeyVaultEncryptionKeyProvider(
            MsOptions.Create(new AzureKeyVaultCryptographyOptions
            {
                VaultUri = realVault.Value.VaultUri,
                CurrentKeyId = "primary",
                KeyNames = new Dictionary<string, string> { ["primary"] = realVault.Value.KeyName },
            }),
            new CryptoRandomGenerator());
        CryptographicKey fromSecondProvider = await secondProvider.GetCurrentKeyAsync();
        Assert.Equal(secondTag, fromSecondProvider.Id);
        Assert.Equal(secondCurrent.Material, fromSecondProvider.Material);
    }

    private static (Uri VaultUri, string KeyName)? TryGetRealVaultConfiguration()
    {
        string? vaultUri = Environment.GetEnvironmentVariable("SHAREDKERNEL_TEST_AZURE_KEYVAULT_URI");
        string? keyName = Environment.GetEnvironmentVariable("SHAREDKERNEL_TEST_AZURE_KEYVAULT_KEY_NAME");

        if (string.IsNullOrWhiteSpace(vaultUri) || string.IsNullOrWhiteSpace(keyName))
        {
            return null;
        }

        return (new Uri(vaultUri), keyName);
    }
}
