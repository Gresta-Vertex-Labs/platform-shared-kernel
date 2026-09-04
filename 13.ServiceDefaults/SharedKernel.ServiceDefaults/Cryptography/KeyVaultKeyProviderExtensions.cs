using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Cryptography.KeyVault.Azure;
using SharedKernel.Cryptography.KeyVault.Azure.Extensions;

namespace SharedKernel.ServiceDefaults.Cryptography;

/// <summary>
/// Composition-root registration for <c>01.Core</c>'s Azure Key Vault Keys
/// <c>IEncryptionKeyProvider</c>/<c>IEnvelopeEncryptionProvider</c> implementation.
/// </summary>
/// <remarks>
/// <para>
/// <b>DISTINCT FROM <see cref="Configuration.KeyVaultConfigurationExtensions.AddSharedKernelKeyVaultConfiguration"/>
/// — DO NOT CONFUSE THE TWO.</b> That method wires Azure Key Vault as an
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> <i>source</i> (secrets read
/// into configuration). <see cref="AddSharedKernelKeyVaultKeyProvider"/> registers Azure Key
/// Vault <i>Keys</i> as the platform's <c>IEncryptionKeyProvider</c>/
/// <c>IEnvelopeEncryptionProvider</c> — the key material used to encrypt/decrypt data (e.g.
/// <c>06.Persistence</c>'s <c>EncryptedValueConverter</c>, <c>02.Caching</c>'s cache-value
/// encryption). A service can use either, both, or neither independently.
/// </para>
/// <para>
/// Implementation is a thin call-through to <c>01.Core</c>'s already-fully-specified
/// <see cref="AzureKeyVaultCryptographyServiceCollectionExtensions.AddSharedKernelAzureKeyVaultCryptography"/> —
/// <c>13.ServiceDefaults</c> never reimplements Key Vault key resolution itself, mirroring the
/// standing "owning domain ships the provider, this domain ships the composition wiring" rule
/// already applied to <c>06.Persistence</c>/<c>08.Storage</c>/<c>09.Search</c>/
/// <c>10.Intelligence</c>/<c>17.Workflows</c>/<c>12.Security.Mtls</c>.
/// </para>
/// </remarks>
public static class KeyVaultKeyProviderExtensions
{
    /// <summary>
    /// Registers <c>01.Core</c>'s <see cref="AzureKeyVaultEncryptionKeyProvider"/> as the
    /// platform's <c>IEncryptionKeyProvider</c>/<c>IEnvelopeEncryptionProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Idempotent: calling this method more than once registers
    /// <see cref="AzureKeyVaultEncryptionKeyProvider"/>/<c>IEncryptionKeyProvider</c>/
    /// <c>IEnvelopeEncryptionProvider</c> exactly once — the second call is a no-op, guarded by
    /// checking whether <see cref="AzureKeyVaultEncryptionKeyProvider"/> is already registered.
    /// This guard exists here, not in <c>01.Core</c>'s own
    /// <c>AddSharedKernelAzureKeyVaultCryptography</c>, which registers unconditionally via
    /// <c>AddSingleton</c> (correct for its own single-call contract) — a bare call-through would
    /// otherwise double-register on a repeated call.
    /// </remarks>
    public static IHostApplicationBuilder AddSharedKernelKeyVaultKeyProvider(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(AzureKeyVaultEncryptionKeyProvider)))
        {
            return builder;
        }

        builder.Services.AddSharedKernelAzureKeyVaultCryptography(builder.Configuration);

        return builder;
    }
}
