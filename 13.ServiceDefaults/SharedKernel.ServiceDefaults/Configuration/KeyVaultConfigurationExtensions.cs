using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SharedKernel.ServiceDefaults.Configuration;

/// <summary>
/// Opt-in Azure Key Vault <see cref="IConfiguration"/> provider, wrapping
/// <c>Azure.Extensions.AspNetCore.Configuration.Secrets</c> — the platform's first secrets-manager
/// configuration integration.
/// </summary>
/// <remarks>
/// Chosen as the first provider because <c>12.Security.Oidc</c> already targets Azure B2C —
/// <see cref="TokenCredential"/>/<see cref="DefaultAzureCredential"/> is already first-class in
/// this platform's identity story. Explicitly the first of a pluggable secrets-provider family
/// (mirrors <c>08.Storage</c>'s S3/OBS multi-cloud precedent) — a future AWS Secrets
/// Manager/HashiCorp Vault provider is an explicit future follow-up, not this method. Entirely
/// opt-in — called explicitly by the consumer after <c>AddServiceDefaults()</c> and before any
/// code reads vault-backed configuration values; no new mandatory dependency on the
/// <c>AddServiceDefaults()</c> path itself.
/// </remarks>
public static class KeyVaultConfigurationExtensions
{
    /// <summary>
    /// Adds Azure Key Vault as an additional <see cref="IConfiguration"/> source.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="vaultUri">The Azure Key Vault URI, e.g. <c>https://my-vault.vault.azure.net/</c>.</param>
    /// <param name="credential">
    /// The credential used to authenticate against the vault. Defaults to
    /// <see cref="DefaultAzureCredential"/> when omitted.
    /// </param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>GATING finding (WO-061/P-398) — empirically confirmed, not assumed:</b> this method's
    /// fail-fast requirement is satisfied by a real, documented characteristic of
    /// <see cref="IHostApplicationBuilder.Configuration"/> itself, not by any special handling in
    /// this method. For every builder shape this package targets (<c>WebApplicationBuilder</c>,
    /// <c>Host.CreateApplicationBuilder</c>), <c>Configuration</c> is a
    /// <see cref="Microsoft.Extensions.Configuration.ConfigurationManager"/>, which — unlike the
    /// classic <see cref="ConfigurationBuilder"/> pattern, where every source's <c>Load()</c> is
    /// deferred until an explicit <c>.Build()</c> call — rebuilds its
    /// <see cref="IConfigurationRoot"/> EAGERLY and SYNCHRONOUSLY on every call to <c>Add(...)</c>.
    /// <c>AddAzureKeyVault(...)</c> below is exactly such a call: its underlying
    /// <c>AzureKeyVaultConfigurationProvider.Load()</c> synchronously enumerates the vault's
    /// secrets (blocking on the async Key Vault client call), and any failure — DNS resolution
    /// failure, connection refused, authentication failure — surfaces as a thrown exception
    /// directly from THIS method call, before it returns. There is no separate "empty
    /// configuration source" fallback path to guard against. Confirmed via a dedicated test against
    /// a local, definitely-unreachable endpoint (a loopback address with no listener, which fails
    /// fast via a connection-refused error rather than a slow DNS timeout) rather than assumed from
    /// documentation alone — see <c>KeyVaultConfigurationExtensionsTests</c>.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder AddSharedKernelKeyVaultConfiguration(
        this IHostApplicationBuilder builder,
        Uri vaultUri,
        TokenCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(vaultUri);

        builder.Configuration.AddAzureKeyVault(vaultUri, credential ?? new DefaultAzureCredential());

        return builder;
    }
}
