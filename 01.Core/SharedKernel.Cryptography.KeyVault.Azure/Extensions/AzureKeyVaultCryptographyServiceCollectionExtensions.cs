using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Cryptography.KeyVault.Azure.Options;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Cryptography.KeyVault.Azure.Extensions;

/// <summary>
/// DI extension methods for registering <c>SharedKernel.Cryptography.KeyVault.Azure</c> services.
/// </summary>
public static class AzureKeyVaultCryptographyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AzureKeyVaultCryptographyOptions"/> (validated — Data Annotations
    /// plus <see cref="AzureKeyVaultCryptographyOptionsValidator"/>'s cross-field checks, both
    /// eagerly evaluated at startup via <c>ValidateOnStart()</c>), <see cref="AzureKeyVaultEncryptionKeyProvider"/>
    /// as <b>all three</b> <see cref="IEncryptionKeyProvider"/>, <see cref="IEnvelopeEncryptionProvider"/>,
    /// and <see cref="IEncryptionKeyProviderProbe"/> — the same singleton instance, resolvable
    /// through any of the three service types — and <see cref="AzureKeyVaultAsymmetricKeyProvider"/>
    /// as <see cref="IAsymmetricKeyProvider"/> (P-494/WO-081) — a <b>distinct</b> singleton, never
    /// the same instance as <see cref="AzureKeyVaultEncryptionKeyProvider"/>, since wrap/unwrap
    /// keys and signing keys are a different Key Vault key usage pattern even when configured in
    /// the same vault.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The root <see cref="IConfiguration"/>. <see cref="AzureKeyVaultCryptographyOptions"/> is
    /// bound from the <see cref="AzureKeyVaultCryptographyOptions.SectionName"/> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Registers no caching decorator — this package ships zero caching of its own. Wrap the
    /// registered <see cref="IEncryptionKeyProvider"/> in <c>SharedKernel.Cryptography</c>'s
    /// <c>CachedEncryptionKeyProvider</c> explicitly if bounded-TTL caching is desired.
    /// </para>
    /// <para>
    /// Also registers <see cref="ISecureRandomGenerator"/>/<see cref="CryptoRandomGenerator"/>
    /// via <c>TryAddSingleton</c> (never overriding an existing registration) so this method is
    /// self-sufficient even when the consumer has not separately called
    /// <c>SharedKernel.Cryptography</c>'s own <c>AddSharedKernelCryptography</c>.
    /// </para>
    /// <para>
    /// <see cref="AzureKeyVaultCryptographyOptions.Credential"/> is never bound from
    /// <paramref name="configuration"/> (a <see cref="Azure.Core.TokenCredential"/> is not a
    /// configuration-bindable POCO) — it defaults to <see cref="Azure.Identity.DefaultAzureCredential"/>
    /// when left unset. A consumer wanting a specific credential sets it via
    /// <c>services.PostConfigure&lt;AzureKeyVaultCryptographyOptions&gt;(o =&gt; o.Credential = myCredential)</c>
    /// after calling this method.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelAzureKeyVaultCryptography(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<AzureKeyVaultCryptographyOptions>(
            configuration.GetSection(AzureKeyVaultCryptographyOptions.SectionName));
        services.AddSingleton<IValidateOptions<AzureKeyVaultCryptographyOptions>, AzureKeyVaultCryptographyOptionsValidator>();

        services.TryAddSingleton<ISecureRandomGenerator, CryptoRandomGenerator>();

        services.AddSingleton<AzureKeyVaultEncryptionKeyProvider>();
        services.AddSingleton<IEncryptionKeyProvider>(sp =>
            sp.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>());
        services.AddSingleton<IEnvelopeEncryptionProvider>(sp =>
            sp.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>());
        services.AddSingleton<IEncryptionKeyProviderProbe>(sp =>
            sp.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>());

        // A DISTINCT singleton from AzureKeyVaultEncryptionKeyProvider — never resolved through
        // it. See AzureKeyVaultAsymmetricKeyProvider's class-level remarks for why signing keys
        // and wrap/unwrap keys are never shared through one class.
        services.AddSingleton<AzureKeyVaultAsymmetricKeyProvider>();
        services.AddSingleton<IAsymmetricKeyProvider>(sp =>
            sp.GetRequiredService<AzureKeyVaultAsymmetricKeyProvider>());

        return services;
    }
}
