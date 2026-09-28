using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SharedKernel.Communication.Internal;

/// <summary>Applies <see cref="ClientTlsOptions"/> to a client's connection handler.</summary>
internal static class ClientTls
{
    /// <summary>Adds the client certificate and the private trust anchors, when configured.</summary>
    public static void Apply(SocketsHttpHandler handler, ClientTlsOptions tls)
    {
        if (tls.HasClientCertificate)
        {
            handler.SslOptions.ClientCertificates = [LoadClientCertificate(tls)];
        }

        if (!string.IsNullOrWhiteSpace(tls.TrustedCertificateAuthoritiesPath))
        {
            var authorities = new X509Certificate2Collection();
            authorities.ImportFromPemFile(tls.TrustedCertificateAuthoritiesPath);
            if (authorities.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Tls:TrustedCertificateAuthoritiesPath '{tls.TrustedCertificateAuthoritiesPath}' holds no PEM certificate.");
            }

            handler.SslOptions.RemoteCertificateValidationCallback =
                (_, certificate, chain, errors) => IsTrusted(certificate, chain, errors, authorities);
        }
    }

    internal static X509Certificate2 LoadClientCertificate(ClientTlsOptions tls)
    {
        string path = tls.CertificatePath!;
        string extension = Path.GetExtension(path);
        if (extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(path, tls.CertificatePassword);
        }

        // PEM: the key in its own file, or after the certificate in the same one.
        X509Certificate2 pem = string.IsNullOrEmpty(tls.CertificatePassword)
            ? X509Certificate2.CreateFromPemFile(path, tls.PrivateKeyPath)
            : X509Certificate2.CreateFromEncryptedPemFile(path, tls.CertificatePassword, tls.PrivateKeyPath);

        if (!OperatingSystem.IsWindows())
        {
            return pem;
        }

        // Windows' TLS stack (SChannel) cannot present a certificate whose key is ephemeral, which a PEM key is.
        using (pem)
        {
            return X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), password: null);
        }
    }

    /// <summary>
    /// The server certificate is valid for the host and chains to one of <paramref name="authorities"/> — and only to
    /// them: the machine's trust store is not consulted.
    /// </summary>
    internal static bool IsTrusted(
        System.Security.Cryptography.X509Certificates.X509Certificate? certificate,
        X509Chain? presentedChain,
        SslPolicyErrors errors,
        X509Certificate2Collection authorities)
    {
        // A name mismatch or a missing certificate is never forgiven; only the chain is judged again, here.
        if (certificate is not X509Certificate2 serverCertificate
            || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(authorities);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        if (presentedChain is not null)
        {
            // The intermediates the server sent.
            foreach (X509ChainElement element in presentedChain.ChainElements)
            {
                chain.ChainPolicy.ExtraStore.Add(element.Certificate);
            }
        }

        return chain.Build(serverCertificate);
    }
}
