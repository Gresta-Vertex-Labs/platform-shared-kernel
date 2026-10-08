using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Secrets;
using SharedKernel.ServiceDefaults.Configuration;

namespace Shop.Billing.Api.Security;

/// <summary>
/// Billing's Key Vault: its secrets as configuration (13.ServiceDefaults.Configuration.KeyVault), and the clients the
/// kernel's Key Vault signing and envelope encryption build (01.Core Cryptography.KeyVault.Azure).
/// </summary>
/// <remarks>
/// With <c>Billing:KeyVault:CaCertificatePath</c> set, the vault is the Lowkey Vault emulator of the Shop platform: it
/// serves the Shop's development certificate, so only the Shop CA is trusted; it accepts any bearer token; and its
/// challenge names no Azure resource, so challenge resource verification is off. Without it, the vault is Azure Key
/// Vault with <see cref="DefaultAzureCredential"/>.
/// </remarks>
public static class BillingKeyVault
{
    public const string UriKey = "Billing:KeyVault:Uri";
    public const string CaCertificatePathKey = "Billing:KeyVault:CaCertificatePath";

    public static WebApplicationBuilder AddBillingKeyVault(this WebApplicationBuilder builder)
    {
        var vaultUri = new Uri(
            builder.Configuration[UriKey]
                ?? throw new InvalidOperationException($"{UriKey} is not configured.")
        );
        string? caPath = builder.Configuration[CaCertificatePathKey];

        TokenCredential credential;
        var secretOptions = new SecretClientOptions();
        var keyOptions = new KeyClientOptions();
        if (caPath is null)
        {
            credential = new DefaultAzureCredential();
        }
        else
        {
            credential = new EmulatorTokenCredential();
            var transport = new HttpClientTransport(
                TrustOnly(X509CertificateLoader.LoadCertificateFromFile(caPath))
            );
            secretOptions.Transport = transport;
            secretOptions.DisableChallengeResourceVerification = true;
            keyOptions.Transport = transport;
            keyOptions.DisableChallengeResourceVerification = true;
        }

        // Secrets become configuration first, so everything registered after sees them (the API key hashes).
        builder.AddSharedKernelKeyVaultConfiguration(vaultUri, credential, secretOptions);

        // The kernel's Key Vault cryptography resolves these from the container.
        builder.Services.AddSingleton(credential);
        builder.Services.AddSingleton(secretOptions);
        builder.Services.AddSingleton(keyOptions);
        return builder;
    }

    private static HttpClientHandler TrustOnly(X509Certificate2 authority) =>
        new()
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
            {
                if (
                    certificate is null
                    || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors)
                        != SslPolicyErrors.None
                )
                {
                    return false;
                }

                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(authority);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return chain.Build(certificate);
            },
        };

    /// <summary>Lowkey Vault checks that a bearer token is present, not what it says.</summary>
    private sealed class EmulatorTokenCredential : TokenCredential
    {
        private static readonly AccessToken Token = new(
            "lowkey-vault-development-token",
            DateTimeOffset.MaxValue
        );

        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken
        ) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(Token);
    }
}
