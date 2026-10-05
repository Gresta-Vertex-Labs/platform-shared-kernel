using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SharedKernel.Communication.Internal;

namespace SharedKernel.Communication.Tests;

public sealed class ClientTlsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("sk-tls-").FullName;

    [Fact]
    public void A_server_certificate_issued_by_a_trusted_authority_is_accepted()
    {
        using X509Certificate2 authority = CreateAuthority("CN=Private CA");
        using X509Certificate2 server = Issue(authority, "CN=inventory");

        ClientTls.IsTrusted(server, null, SslPolicyErrors.RemoteCertificateChainErrors, [authority]).Should().BeTrue();
    }

    [Fact]
    public void A_server_certificate_from_another_authority_is_refused()
    {
        using X509Certificate2 authority = CreateAuthority("CN=Private CA");
        using X509Certificate2 other = CreateAuthority("CN=Other CA");
        using X509Certificate2 server = Issue(other, "CN=inventory");

        ClientTls.IsTrusted(server, null, SslPolicyErrors.RemoteCertificateChainErrors, [authority]).Should().BeFalse();
    }

    [Fact]
    public void A_name_mismatch_is_never_forgiven()
    {
        using X509Certificate2 authority = CreateAuthority("CN=Private CA");
        using X509Certificate2 server = Issue(authority, "CN=inventory");

        ClientTls.IsTrusted(server, null, SslPolicyErrors.RemoteCertificateNameMismatch, [authority]).Should().BeFalse();
    }

    [Fact]
    public void A_PEM_certificate_and_key_load_with_the_private_key()
    {
        using X509Certificate2 authority = CreateAuthority("CN=Private CA");
        using X509Certificate2 client = Issue(authority, "CN=checkout");
        string certificatePath = Write("tls.crt", client.ExportCertificatePem());
        string keyPath = Write("tls.key", client.GetECDsaPrivateKey()!.ExportPkcs8PrivateKeyPem());

        using X509Certificate2 loaded = ClientTls.LoadClientCertificate(new ClientTlsOptions { CertificatePath = certificatePath, PrivateKeyPath = keyPath });

        loaded.HasPrivateKey.Should().BeTrue();
        loaded.Subject.Should().Be("CN=checkout");
    }

    [Fact]
    public void A_PKCS12_file_loads_with_its_password()
    {
        using X509Certificate2 authority = CreateAuthority("CN=Private CA");
        using X509Certificate2 client = Issue(authority, "CN=checkout");
        string path = Path.Combine(_directory, "client.pfx");
        File.WriteAllBytes(path, client.Export(X509ContentType.Pkcs12, "p@ss"));

        using X509Certificate2 loaded = ClientTls.LoadClientCertificate(new ClientTlsOptions { CertificatePath = path, CertificatePassword = "p@ss" });

        loaded.HasPrivateKey.Should().BeTrue();
    }

    [Fact]
    public void The_handler_gets_the_client_certificate_and_the_private_trust()
    {
        using X509Certificate2 authority = CreateAuthority("CN=Private CA");
        using X509Certificate2 client = Issue(authority, "CN=checkout");
        using var handler = new SocketsHttpHandler();
        var options = new ClientTlsOptions
        {
            CertificatePath = Write("tls.crt", client.ExportCertificatePem()),
            PrivateKeyPath = Write("tls.key", client.GetECDsaPrivateKey()!.ExportPkcs8PrivateKeyPem()),
            TrustedCertificateAuthoritiesPath = Write("ca.crt", authority.ExportCertificatePem()),
        };

        ClientTls.Apply(handler, options);

        handler.SslOptions.ClientCertificates!.Count.Should().Be(1);
        handler.SslOptions.RemoteCertificateValidationCallback.Should().NotBeNull();
    }

    [Fact]
    public void Nothing_is_changed_without_TLS_settings()
    {
        using var handler = new SocketsHttpHandler();

        ClientTls.Apply(handler, new ClientTlsOptions());

        handler.SslOptions.ClientCertificates.Should().BeNull();
        handler.SslOptions.RemoteCertificateValidationCallback.Should().BeNull();
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string name, string content)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static X509Certificate2 CreateAuthority(string subject)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private static X509Certificate2 Issue(X509Certificate2 authority, string subject)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        using X509Certificate2 issued = request.Create(
            authority,
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddDays(7),
            RandomNumberGenerator.GetBytes(8));
        return issued.CopyWithPrivateKey(key);
    }
}
