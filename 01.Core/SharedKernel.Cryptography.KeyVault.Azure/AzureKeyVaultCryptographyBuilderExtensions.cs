using System.Diagnostics.CodeAnalysis;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Health;

namespace SharedKernel.Cryptography.KeyVault.Azure;

/// <summary>Registers Azure Key Vault key providers with <c>SharedKernel.Cryptography</c>.</summary>
/// <remarks>
/// Both methods authenticate with the <see cref="TokenCredential"/> registered in the container, or a shared
/// <see cref="DefaultAzureCredential"/> when none is. In production, register a specific credential such as
/// <see cref="ManagedIdentityCredential"/> first; it starts faster and cannot pick up a developer's identity.
/// </remarks>
public static class AzureKeyVaultCryptographyBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="AzureKeyVaultEncryptionKeyProvider"/> as <see cref="IEncryptionKeyProvider"/>,
    /// <see cref="IEnvelopeEncryptionProvider"/> and a readiness probe named <see cref="AzureKeyVaultEncryptionKeyProvider.ReadinessProbeName"/>, with validated
    /// <see cref="AzureKeyVaultEncryptionOptions"/>.
    /// </summary>
    /// <param name="builder">The builder returned by <c>AddSharedKernelCryptography</c>.</param>
    /// <param name="configuration">The configuration root; options bind from <c>SharedKernel:Cryptography:KeyVault:Azure:Encryption</c>.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    /// <b>Never wired into persistence on the service's behalf.</b> This call does not turn on <c>06.Persistence</c>'s
    /// field encryption: a service wanting KMS-backed column encryption opts in with <c>UseFieldEncryption(…)</c> in its
    /// own persistence registration. The provider does fill the one unkeyed <see cref="IEncryptionKeyProvider"/> slot,
    /// which <c>06.Persistence</c> uses as the root of its ETag key (P-562 X4) when no
    /// <c>ISynchronousEncryptionKeyProvider</c> is registered — through an HKDF subkey of its own purpose, so that
    /// sharing is safe and a service with ETags needs no second provider. (Ported in P-579 from the note main added to the
    /// deleted <c>SharedKernel.ServiceDefaults.Cryptography.KeyVault</c>.)
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddSharedKernelCryptography(configuration)
    ///     .AddAzureKeyVaultEncryption(configuration)
    ///     .AddSymmetricEncryption()
    ///     .AddEnvelopeEncryption();
    /// </code>
    /// </example>
    [RequiresUnreferencedCode("Binds configuration by reflection.")]
    [RequiresDynamicCode("Binds configuration by reflection.")]
    public static ICryptographyBuilder AddAzureKeyVaultEncryption(this ICryptographyBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        IServiceCollection services = builder.Services;
        services.AddValidatedOptions<AzureKeyVaultEncryptionOptions, AzureKeyVaultEncryptionOptionsValidator>(
            configuration,
            validateDataAnnotations: true);
        AddShared(services);

        services.TryAddSingleton(sp =>
        {
            AzureKeyVaultEncryptionOptions options = sp.GetRequiredService<IOptions<AzureKeyVaultEncryptionOptions>>().Value;
            TokenCredential credential = sp.GetRequiredService<TokenCredential>();
            return new AzureKeyVaultEncryptionKeyProvider(
                Microsoft.Extensions.Options.Options.Create(options),
                new KeyClient(options.VaultUri!, credential),
                new SecretClient(options.VaultUri!, credential),
                sp.GetRequiredService<ISecureRandomGenerator>(),
                sp.GetRequiredService<TimeProvider>());
        });
        services.TryAddSingleton<IEncryptionKeyProvider>(sp => sp.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>());
        services.TryAddSingleton<IEnvelopeEncryptionProvider>(sp => sp.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReadinessProbe, AzureKeyVaultEncryptionKeyProvider>(
            sp => sp.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>()));

        return builder;
    }

    /// <summary>
    /// Registers <see cref="AzureKeyVaultSigningKeyProvider"/> as <see cref="ISigningKeyProvider"/>, with validated
    /// <see cref="AzureKeyVaultSigningOptions"/>.
    /// </summary>
    /// <param name="builder">The builder returned by <c>AddSharedKernelCryptography</c>.</param>
    /// <param name="configuration">The configuration root; options bind from <c>SharedKernel:Cryptography:KeyVault:Azure:Signing</c>.</param>
    /// <returns>The same builder.</returns>
    /// <example>
    /// <code>
    /// services.AddSharedKernelCryptography(configuration)
    ///     .AddAzureKeyVaultSigning(configuration)
    ///     .AddAsymmetricSigning();
    /// </code>
    /// </example>
    [RequiresUnreferencedCode("Binds configuration by reflection.")]
    [RequiresDynamicCode("Binds configuration by reflection.")]
    public static ICryptographyBuilder AddAzureKeyVaultSigning(this ICryptographyBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        IServiceCollection services = builder.Services;
        services.AddValidatedOptions<AzureKeyVaultSigningOptions, AzureKeyVaultSigningOptionsValidator>(
            configuration,
            validateDataAnnotations: true);
        AddShared(services);

        services.TryAddSingleton(sp =>
        {
            AzureKeyVaultSigningOptions options = sp.GetRequiredService<IOptions<AzureKeyVaultSigningOptions>>().Value;
            return new AzureKeyVaultSigningKeyProvider(
                Microsoft.Extensions.Options.Options.Create(options),
                new KeyClient(options.VaultUri!, sp.GetRequiredService<TokenCredential>()),
                sp.GetRequiredService<TimeProvider>());
        });
        services.TryAddSingleton<ISigningKeyProvider>(sp => sp.GetRequiredService<AzureKeyVaultSigningKeyProvider>());

        return builder;
    }

    private static void AddShared(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        services.TryAddSingleton<ISecureRandomGenerator, SecureRandomGenerator>();
    }
}
