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
    /// <para>
    /// Every registration in this method uses <c>TryAddSingleton</c> — calling this method more
    /// than once never double-registers, and a consumer registration made <b>before</b> this call
    /// always wins over the platform default. The one exception is
    /// <see cref="AzureKeyVaultCryptographyOptionsValidator"/>'s own
    /// <see cref="IValidateOptions{TOptions}"/> registration below, which — like
    /// <c>SharedKernel.Validation</c>'s <c>INationalIdValidator</c> — is a genuine,
    /// intentional multi-implementation collection: the immediately preceding
    /// <c>SharedKernel.Configuration.Extensions.OptionsExtensions.AddValidatedOptions{TOptions}</c>
    /// call already registers its own
    /// <c>DataAnnotationValidateOptions&lt;AzureKeyVaultCryptographyOptions&gt;</c>
    /// against the identical <see cref="IValidateOptions{TOptions}"/> service type via the BCL's own
    /// <c>ValidateDataAnnotations()</c>, and the options-validation pipeline is designed to run
    /// <b>every</b> registered <see cref="IValidateOptions{TOptions}"/> for a type, not just one. A
    /// plain <c>TryAddSingleton</c> here would see that service type already claimed and silently
    /// never register this validator's cross-field checks — confirmed as a real regression during
    /// SK.01.P518 implementation (two host-startup tests stopped throwing). The fix is
    /// <c>TryAddEnumerable(ServiceDescriptor.Singleton&lt;IValidateOptions&lt;AzureKeyVaultCryptographyOptions&gt;, AzureKeyVaultCryptographyOptionsValidator&gt;())</c>
    /// — it still prevents this exact (service, implementation) pair from registering twice across
    /// repeated calls to this method, while never suppressing the DataAnnotations validator
    /// registered alongside it.
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<AzureKeyVaultCryptographyOptions>, AzureKeyVaultCryptographyOptionsValidator>());

        services.TryAddSingleton<ISecureRandomGenerator, CryptoRandomGenerator>();

        services.TryAddSingleton<AzureKeyVaultEncryptionKeyProvider>();
        services.TryAddSingleton<IEncryptionKeyProvider>(sp =>
            sp.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>());
        services.TryAddSingleton<IEnvelopeEncryptionProvider>(sp =>
            sp.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>());
        services.TryAddSingleton<IEncryptionKeyProviderProbe>(sp =>
            sp.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>());

        // A DISTINCT singleton from AzureKeyVaultEncryptionKeyProvider — never resolved through
        // it. See AzureKeyVaultAsymmetricKeyProvider's class-level remarks for why signing keys
        // and wrap/unwrap keys are never shared through one class.
        services.TryAddSingleton<AzureKeyVaultAsymmetricKeyProvider>();
        services.TryAddSingleton<IAsymmetricKeyProvider>(sp =>
            sp.GetRequiredService<AzureKeyVaultAsymmetricKeyProvider>());

        return services;
    }
}
