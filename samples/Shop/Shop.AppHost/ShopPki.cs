using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Shop.AppHost;

/// <summary>
/// The Shop's development PKI: a CA, a server certificate for <c>localhost</c> and client certificates for the services
/// that call each other over mutual TLS. Created once under the temp folder and reused; DEVELOPMENT ONLY.
/// </summary>
public sealed class ShopPki
{
    public const string Password = "shop-dev-pki";

    private static readonly Lock Gate = new();

    private ShopPki(string directory) => Directory = directory;

    public string Directory { get; }

    /// <summary>The CA certificate (public part, DER).</summary>
    public string CaCertificatePath => Path.Combine(Directory, "ca.cer");

    /// <summary>The CA certificate as PEM, for clients that read a PEM trust bundle.</summary>
    public string CaPemPath => Path.Combine(Directory, "ca.pem");

    /// <summary>The <c>localhost</c> server certificate with its key (PFX, <see cref="Password"/>).</summary>
    public string ServerCertificatePath => Path.Combine(Directory, "server.pfx");

    /// <summary>A client certificate with its key (PFX, <see cref="Password"/>).</summary>
    public string ClientCertificatePath(string client) =>
        Path.Combine(Directory, $"client-{client}.pfx");

    /// <summary>The SHA-256 thumbprint of a client certificate, as an mTLS allow-list keys it.</summary>
    public string ClientThumbprint(string client)
    {
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            ClientCertificatePath(client),
            Password
        );
        return certificate.GetCertHashString(HashAlgorithmName.SHA256);
    }

    /// <summary>The PKI, created on first use with a client certificate for each of <paramref name="clients"/>.</summary>
    public static ShopPki Ensure(params string[] clients)
    {
        var pki = new ShopPki(Path.Combine(Path.GetTempPath(), "shop-pki"));
        lock (Gate)
        {
            System.IO.Directory.CreateDirectory(pki.Directory);
            string caPfx = Path.Combine(pki.Directory, "ca.pfx");
            if (
                !File.Exists(caPfx)
                || !File.Exists(pki.ServerCertificatePath)
                || !File.Exists(pki.CaPemPath)
            )
            {
                using var ca = CreateCa();
                File.WriteAllBytes(caPfx, ca.Export(X509ContentType.Pfx, Password));
                File.WriteAllBytes(pki.CaCertificatePath, ca.Export(X509ContentType.Cert));
                File.WriteAllText(pki.CaPemPath, ca.ExportCertificatePem());
                using var server = Issue(ca, "localhost", server: true);
                File.WriteAllBytes(
                    pki.ServerCertificatePath,
                    server.Export(X509ContentType.Pfx, Password)
                );
                foreach (
                    string stale in System.IO.Directory.EnumerateFiles(
                        pki.Directory,
                        "client-*.pfx"
                    )
                )
                {
                    File.Delete(stale);
                }
            }

            using var issuer = X509CertificateLoader.LoadPkcs12FromFile(
                caPfx,
                Password,
                X509KeyStorageFlags.Exportable
            );
            foreach (
                string client in clients.Where(c => !File.Exists(pki.ClientCertificatePath(c)))
            )
            {
                using var certificate = Issue(issuer, client, server: false);
                File.WriteAllBytes(
                    pki.ClientCertificatePath(client),
                    certificate.Export(X509ContentType.Pfx, Password)
                );
            }
        }

        return pki;
    }

    private static X509Certificate2 CreateCa()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            "CN=Shop Development CA",
            key,
            HashAlgorithmName.SHA256
        );
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                true
            )
        );
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false)
        );
        var now = TimeProvider.System.GetUtcNow();
        return request.CreateSelfSigned(now.AddDays(-1), now.AddYears(2));
    }

    private static X509Certificate2 Issue(X509Certificate2 ca, string name, bool server)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true)
        );
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [server ? new Oid("1.3.6.1.5.5.7.3.1") : new Oid("1.3.6.1.5.5.7.3.2")],
                true
            )
        );
        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, true, false)
        );
        if (server)
        {
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            names.AddIpAddress(IPAddress.IPv6Loopback);
            request.CertificateExtensions.Add(names.Build());
        }

        var now = TimeProvider.System.GetUtcNow();
        byte[] serial = RandomNumberGenerator.GetBytes(16);
        using var issued = request.Create(ca, now.AddDays(-1), now.AddYears(1), serial);
        return issued.CopyWithPrivateKey(key);
    }
}
