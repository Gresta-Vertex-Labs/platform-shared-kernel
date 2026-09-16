using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.KeyVault.Azure;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.ServiceDefaults.Cryptography;

/// <summary>
/// Composition-root registration for <c>01.Core</c>'s Azure Key Vault encryption-key provider.
/// </summary>
/// <remarks>
/// <para>
/// <b>DISTINCT FROM <c>Configuration.KeyVaultConfigurationExtensions.AddSharedKernelKeyVaultConfiguration</c>
/// — DO NOT CONFUSE THE TWO.</b> That method wires Azure Key Vault as an
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> <i>source</i> (secrets read
/// into configuration). <see cref="AddSharedKernelKeyVaultKeyProvider"/> registers Azure Key
/// Vault as the platform's <see cref="IEncryptionKeyProvider"/>/<see cref="IEnvelopeEncryptionProvider"/> —
/// the key material used to encrypt and decrypt data. A service can use either, both, or neither
/// independently.
/// </para>
/// <para>
/// Implementation is a thin call-through to <c>01.Core</c>'s
/// <see cref="CryptographyServiceCollectionExtensions.AddSharedKernelCryptography"/> and
/// <see cref="AzureKeyVaultCryptographyBuilderExtensions.AddAzureKeyVaultEncryption"/>, bound to the host's own
/// configuration — <c>13.ServiceDefaults</c> never reimplements Key Vault key resolution itself, mirroring the
/// standing "owning domain ships the provider, this domain ships the composition wiring" rule.
/// </para>
/// </remarks>
public static class KeyVaultKeyProviderExtensions
{
    /// <summary>
    /// Registers <c>01.Core</c>'s key-free cryptography services and its
    /// <see cref="AzureKeyVaultEncryptionKeyProvider"/> as the host's <see cref="IEncryptionKeyProvider"/>,
    /// <see cref="IEnvelopeEncryptionProvider"/> and <see cref="IEncryptionKeyProviderProbe"/>, bound from the
    /// host's configuration section <c>SharedKernel:Cryptography:KeyVault:Azure:Encryption</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>
    /// The cryptography builder, to chain the services that consume the provider — for example
    /// <c>.AddSymmetricEncryption()</c> or <c>.AddEnvelopeEncryption()</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Idempotent: every registration uses <c>TryAdd</c>, so calling this method more than once registers the
    /// provider once.
    /// </para>
    /// <para>
    /// <b>No caching wrapper.</b> <see cref="AzureKeyVaultEncryptionKeyProvider"/> caches data keys and its
    /// version list itself (see <see cref="AzureKeyVaultEncryptionOptions.RefreshInterval"/>), so it is never
    /// wrapped in <see cref="CachedEncryptionKeyProvider"/>. The readiness probe reaches the same singleton and
    /// always observes live vault state.
    /// </para>
    /// <para>
    /// <b>Asynchronous callers only.</b> The provider is not an <see cref="ISynchronousEncryptionKeyProvider"/>, so
    /// synchronous code paths — <c>06.Persistence</c> value converters, <c>07.Messaging</c> payload serializers —
    /// cannot use it; <see cref="ISynchronousSymmetricEncryptionService"/> fails to resolve against it rather than
    /// blocking a thread. Give those paths their own synchronous provider.
    /// </para>
    /// <para>
    /// <b>STANDING DESIGN NOTE — DO NOT MAKE THIS AUTOMATIC.</b> This method never wires the provider into
    /// <c>06.Persistence</c>'s encryption on a consuming service's behalf. A service wanting KMS-backed
    /// persistence-layer encryption opts in explicitly in its own <c>06.Persistence</c> builder chain, so
    /// <c>.WithEncryption()</c> and this method can never silently collide on the one unkeyed
    /// <see cref="IEncryptionKeyProvider"/> slot.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.AddSharedKernelKeyVaultKeyProvider()
    ///     .AddSymmetricEncryption()
    ///     .AddEnvelopeEncryption();
    /// </code>
    /// </example>
    [RequiresUnreferencedCode("Binds configuration by reflection.")]
    [RequiresDynamicCode("Binds configuration by reflection.")]
    public static ICryptographyBuilder AddSharedKernelKeyVaultKeyProvider(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Services
            .AddSharedKernelCryptography(builder.Configuration)
            .AddAzureKeyVaultEncryption(builder.Configuration);
    }
}
