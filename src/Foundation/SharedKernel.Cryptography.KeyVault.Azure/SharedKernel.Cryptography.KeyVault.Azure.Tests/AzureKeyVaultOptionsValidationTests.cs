using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Signing;
using Xunit;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests;

public sealed class AzureKeyVaultOptionsValidationTests
{
    private const string Encryption = "SharedKernel:Cryptography:KeyVault:Azure:Encryption";
    private const string Signing = "SharedKernel:Cryptography:KeyVault:Azure:Signing";

    [Fact]
    public void SectionNames_Always_MatchDocumentedPaths()
    {
        Assert.Equal(Encryption, AzureKeyVaultEncryptionOptions.SectionName);
        Assert.Equal(Signing, AzureKeyVaultSigningOptions.SectionName);
    }

    [Fact]
    public void EncryptionOptions_ValidConfiguration_Binds()
    {
        Dictionary<string, string?> settings = EncryptionSettings();
        settings[$"{Encryption}:PreviousMasterKeyNames:0"] = "orders-kek-2025";
        settings[$"{Encryption}:RefreshInterval"] = "00:02:00";

        AzureKeyVaultEncryptionOptions options = ResolveEncryption(settings);

        Assert.Equal(new Uri("https://contoso.vault.azure.net/"), options.VaultUri);
        Assert.Equal("orders-kek", options.MasterKeyName);
        Assert.Equal("orders-data-keys", options.DataKeySecretName);
        Assert.Equal(["orders-kek-2025"], options.PreviousMasterKeyNames);
        Assert.Equal(TimeSpan.FromMinutes(2), options.RefreshInterval);
    }

    [Fact]
    public void EncryptionOptions_RefreshIntervalOmitted_DefaultsToFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), ResolveEncryption(EncryptionSettings()).RefreshInterval);
    }

    [Theory]
    [InlineData("RefreshInterval", "00:00:01")]
    [InlineData("RefreshInterval", "1.00:00:00")]
    [InlineData("MasterKeyName", "a")]
    [InlineData("VaultUri", "HTTPS://contoso.vault.azure.net/")]
    public void EncryptionOptions_ValueAtBound_IsValid(string key, string value)
    {
        Dictionary<string, string?> settings = EncryptionSettings();
        settings[$"{Encryption}:{key}"] = value;

        Assert.NotNull(ResolveEncryption(settings));
    }

    [Theory]
    [InlineData("VaultUri", null)]
    [InlineData("VaultUri", "http://contoso.vault.azure.net/")]
    [InlineData("MasterKeyName", null)]
    [InlineData("MasterKeyName", "orders_kek")]
    [InlineData("MasterKeyName", "orders kek")]
    [InlineData("MasterKeyName", "orders/kek")]
    [InlineData("DataKeySecretName", null)]
    [InlineData("DataKeySecretName", "orders.data.keys")]
    [InlineData("PreviousMasterKeyNames:0", "old kek")]
    [InlineData("PreviousMasterKeyNames:1", "old/kek")]
    [InlineData("RefreshInterval", "00:00:00")]
    [InlineData("RefreshInterval", "00:00:00.5")]
    [InlineData("RefreshInterval", "1.00:00:01")]
    public void EncryptionOptions_InvalidValue_ThrowsOptionsValidationException(string key, string? value)
    {
        Dictionary<string, string?> settings = EncryptionSettings();
        settings[$"{Encryption}:PreviousMasterKeyNames:0"] = "valid-old-kek";
        if (value is null)
        {
            settings.Remove($"{Encryption}:{key}");
        }
        else
        {
            settings[$"{Encryption}:{key}"] = value;
        }

        Assert.Throws<OptionsValidationException>(() => ResolveEncryption(settings));
    }

    [Theory]
    [InlineData("contoso.vault.azure.net")]
    [InlineData("/vault")]
    public void EncryptionOptions_RelativeVaultUri_ThrowsOptionsValidationException(string vaultUri)
    {
        Dictionary<string, string?> settings = EncryptionSettings();
        settings[$"{Encryption}:VaultUri"] = vaultUri;

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => ResolveEncryption(settings));
        Assert.Contains(exception.Failures, f => f.Contains("absolute https URI", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("contoso.vault.azure.net")]
    [InlineData("/vault")]
    public void SigningOptions_RelativeVaultUri_ThrowsOptionsValidationException(string vaultUri)
    {
        Dictionary<string, string?> settings = SigningSettings();
        settings[$"{Signing}:VaultUri"] = vaultUri;

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => ResolveSigning(settings));
        Assert.Contains(exception.Failures, f => f.Contains("absolute https URI", StringComparison.Ordinal));
    }

    [Fact]
    public void EncryptionOptions_MasterKeyNameTooLong_ThrowsOptionsValidationException()
    {
        Dictionary<string, string?> settings = EncryptionSettings();
        settings[$"{Encryption}:MasterKeyName"] = new string('k', 128);

        Assert.Throws<OptionsValidationException>(() => ResolveEncryption(settings));
    }

    [Fact]
    public void EncryptionProvider_InvalidOptions_ThrowsOptionsValidationExceptionOnResolve()
    {
        Dictionary<string, string?> settings = EncryptionSettings();
        settings.Remove($"{Encryption}:MasterKeyName");
        using ServiceProvider provider = BuildEncryptionProvider(settings);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<SharedKernel.Cryptography.Symmetric.IEncryptionKeyProvider>());
    }

    [Fact]
    public void SigningOptions_ValidConfiguration_BindsAlgorithmFromString()
    {
        Dictionary<string, string?> settings = SigningSettings();
        settings[$"{Signing}:Keys:tokens-2026:KeyName"] = "token-signing";
        settings[$"{Signing}:Keys:tokens-2026:KeyVersion"] = "8f1c0123456789abcdef0123456789ab";
        settings[$"{Signing}:Keys:tokens-2026:Algorithm"] = "PS256";

        AzureKeyVaultSigningOptions options = ResolveSigning(settings);

        Assert.Equal(TimeSpan.FromHours(1), options.RefreshInterval);
        Assert.Equal(2, options.Keys.Count);
        Assert.Equal("webhook-signing", options.Keys["webhooks"].KeyName);
        Assert.Equal(SignatureAlgorithm.ES256, options.Keys["webhooks"].Algorithm);
        Assert.Null(options.Keys["webhooks"].KeyVersion);
        Assert.Equal(SignatureAlgorithm.PS256, options.Keys["tokens-2026"].Algorithm);
        Assert.Equal("8f1c0123456789abcdef0123456789ab", options.Keys["tokens-2026"].KeyVersion);
    }

    [Theory]
    [InlineData("RefreshInterval", "00:01:00")]
    [InlineData("RefreshInterval", "1.00:00:00")]
    [InlineData("Keys:webhooks:Algorithm", "ES512")]
    public void SigningOptions_ValueAtBound_IsValid(string key, string value)
    {
        Dictionary<string, string?> settings = SigningSettings();
        settings[$"{Signing}:{key}"] = value;

        Assert.NotNull(ResolveSigning(settings));
    }

    [Theory]
    [InlineData("VaultUri", null)]
    [InlineData("VaultUri", "http://contoso.vault.azure.net/")]
    [InlineData("Keys:webhooks:KeyName", null)]
    [InlineData("Keys:webhooks:KeyName", "webhook_signing")]
    [InlineData("Keys:webhooks:Algorithm", null)]
    [InlineData("Keys:webhooks:Algorithm", "42")]
    [InlineData("Keys:webhooks:KeyVersion", "v1")]
    [InlineData("Keys:webhooks:KeyVersion", "8F1C0123456789ABCDEF0123456789AB")]
    [InlineData("Keys:webhooks:KeyVersion", "8f1c0123456789abcdef0123456789a")]
    [InlineData("RefreshInterval", "00:00:59")]
    [InlineData("RefreshInterval", "00:00:00")]
    [InlineData("RefreshInterval", "1.00:00:01")]
    public void SigningOptions_InvalidValue_ThrowsOptionsValidationException(string key, string? value)
    {
        Dictionary<string, string?> settings = SigningSettings();
        if (value is null)
        {
            settings.Remove($"{Signing}:{key}");
        }
        else
        {
            settings[$"{Signing}:{key}"] = value;
        }

        Assert.Throws<OptionsValidationException>(() => ResolveSigning(settings));
    }

    [Fact]
    public void SigningOptions_NoKeys_ThrowsOptionsValidationException()
    {
        var settings = new Dictionary<string, string?> { [$"{Signing}:VaultUri"] = "https://contoso.vault.azure.net/" };

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => ResolveSigning(settings));

        Assert.Contains(exception.Failures, f => f.Contains("at least one key", StringComparison.Ordinal));
    }

    [Fact]
    public void SigningOptions_InvalidKey_FailureNamesKeyId()
    {
        Dictionary<string, string?> settings = SigningSettings();
        settings.Remove($"{Signing}:Keys:webhooks:Algorithm");

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => ResolveSigning(settings));

        Assert.Contains(exception.Failures, f => f.Contains("webhooks", StringComparison.Ordinal));
    }

    [Fact]
    public void SigningProvider_InvalidOptions_ThrowsOptionsValidationExceptionOnResolve()
    {
        Dictionary<string, string?> settings = SigningSettings();
        settings[$"{Signing}:VaultUri"] = "http://contoso.vault.azure.net/";
        using ServiceProvider provider = BuildSigningProvider(settings);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<ISigningKeyProvider>());
    }

    internal static Dictionary<string, string?> EncryptionSettings() => new()
    {
        [$"{Encryption}:VaultUri"] = "https://contoso.vault.azure.net/",
        [$"{Encryption}:MasterKeyName"] = "orders-kek",
        [$"{Encryption}:DataKeySecretName"] = "orders-data-keys",
    };

    internal static Dictionary<string, string?> SigningSettings() => new()
    {
        [$"{Signing}:VaultUri"] = "https://contoso.vault.azure.net/",
        [$"{Signing}:Keys:webhooks:KeyName"] = "webhook-signing",
        [$"{Signing}:Keys:webhooks:Algorithm"] = "ES256",
    };

    private static AzureKeyVaultEncryptionOptions ResolveEncryption(Dictionary<string, string?> settings)
    {
        using ServiceProvider provider = BuildEncryptionProvider(settings);
        return provider.GetRequiredService<IOptions<AzureKeyVaultEncryptionOptions>>().Value;
    }

    private static AzureKeyVaultSigningOptions ResolveSigning(Dictionary<string, string?> settings)
    {
        using ServiceProvider provider = BuildSigningProvider(settings);
        return provider.GetRequiredService<IOptions<AzureKeyVaultSigningOptions>>().Value;
    }

    private static ServiceProvider BuildEncryptionProvider(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<global::Azure.Core.TokenCredential>(new Fakes.FakeTokenCredential());
        services.AddSharedKernelCryptography(configuration).AddAzureKeyVaultEncryption(configuration);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildSigningProvider(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<global::Azure.Core.TokenCredential>(new Fakes.FakeTokenCredential());
        services.AddSharedKernelCryptography(configuration).AddAzureKeyVaultSigning(configuration);
        return services.BuildServiceProvider();
    }
}
