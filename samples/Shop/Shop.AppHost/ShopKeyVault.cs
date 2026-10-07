using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Shop.AppHost;

/// <summary>
/// The platform's Key Vault: Lowkey Vault, an Azure Key Vault emulator, serving the Shop's development certificate. The
/// AppHost provisions it the way infrastructure-as-code would provision a real vault: the keys Billing signs and wraps
/// with, and the secrets Billing reads as configuration. DEVELOPMENT-ONLY values throughout.
/// </summary>
public static class ShopKeyVault
{
    public const string InvoiceSigningKey = "invoice-signing";
    public const string MasterKey = "billing-master";

    /// <summary>The secret Billing signs the Contoso merchant's webhooks with (the merchant verifies with it too).</summary>
    public const string MerchantWebhookSecret = "shop-dev-merchant-webhook-secret-0001";

    private const string ApiVersion = "api-version=7.5";

    public static IResourceBuilder<ContainerResource> Add(
        IDistributedApplicationBuilder builder,
        ShopPki pki
    )
    {
        // Lowkey Vault is a Spring Boot app: it serves the Shop's server certificate (ECDSA, hence the cipher list), and
        // with relaxed ports it answers on whatever host port Aspire maps.
        var vault = builder
            .AddContainer(ShopResources.KeyVault, "nagyesta/lowkey-vault", "7.3.112")
            .WithBindMount(pki.Directory, "/pki", isReadOnly: true)
            .WithEnvironment(
                "LOWKEY_ARGS",
                string.Join(
                    ' ',
                    "--LOWKEY_VAULT_RELAXED_PORTS=true",
                    "--server.ssl.key-store=/pki/server.pfx",
                    "--server.ssl.key-store-type=PKCS12",
                    $"--server.ssl.key-store-password={ShopPki.Password}",
                    "--server.ssl.key-alias=1",
                    "--server.ssl.ciphers=TLS_AES_128_GCM_SHA256,TLS_AES_256_GCM_SHA384,TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384"
                )
            )
            .WithHttpsEndpoint(targetPort: 8443, name: "https")
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithHttpHealthCheck("/ping", endpointName: "http");

        // Provision the vault before any service that waits for it starts. The Key Vault REST API directly: Lowkey Vault
        // only checks that a bearer token is present.
        builder.Eventing.Subscribe<ResourceReadyEvent>(
            vault.Resource,
            async (_, cancellationToken) =>
            {
                using var http = new HttpClient(TrustOnly(pki.CaCertificatePath))
                {
                    BaseAddress = new Uri(vault.GetEndpoint("https").Url),
                };
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    "lowkey"
                );

                // Each key may do only its job (Lowkey Vault, unlike Azure, grants no operation by default).
                var keys = new (string Name, string[] Operations)[]
                {
                    (InvoiceSigningKey, ["sign", "verify"]),
                    (MasterKey, ["encrypt", "decrypt", "wrapKey", "unwrapKey"]),
                };
                foreach (var (name, operations) in keys)
                {
                    (
                        await http.PostAsJsonAsync(
                            $"/keys/{name}/create?{ApiVersion}",
                            new
                            {
                                kty = "RSA",
                                key_size = 2048,
                                key_ops = operations,
                            },
                            cancellationToken
                        )
                    ).EnsureSuccessStatusCode();
                }

                var secrets = ShopResources
                    .ApiKeys.All.Select(k => ($"Billing--ApiKeys--{k.KeyId}--Hash", HashOf(k.Key)))
                    .Append(
                        (
                            $"Billing--Webhooks--{ShopResources.Identity.ContosoTenant}--Secret",
                            MerchantWebhookSecret
                        )
                    );
                foreach (var (name, value) in secrets)
                {
                    (
                        await http.PutAsJsonAsync(
                            $"/secrets/{name}?{ApiVersion}",
                            new { value },
                            cancellationToken
                        )
                    ).EnsureSuccessStatusCode();
                }
            }
        );

        return vault;
    }

    /// <summary>How SharedKernel.Security.ApiKey stores a key: Base64Url(SHA-256(ASCII key)).</summary>
    private static string HashOf(string key) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(key)));

    private static HttpClientHandler TrustOnly(string caPath)
    {
        var authority = X509CertificateLoader.LoadCertificateFromFile(caPath);
        return new HttpClientHandler
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
    }
}
