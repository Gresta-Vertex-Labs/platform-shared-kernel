using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SharedKernel.Security.Mtls.Tests.TestSupport;

// Builds certificate chains the shared MtlsTestCertificateBuilder cannot: leaves with a chosen extended key usage or
// validity window issued by a long-lived CA.
internal sealed class TestCertificateAuthority : IDisposable
{
    public const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
    public const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    private static readonly DateTimeOffset DefaultNotBefore = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DefaultNotAfter = new(2034, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ECDsa _key;

    private TestCertificateAuthority(X509Certificate2 certificate, ECDsa key)
    {
        Certificate = certificate;
        _key = key;
    }

    public X509Certificate2 Certificate { get; }

    public static TestCertificateAuthority Create(string name = "CN=Mtls Tests Root CA")
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return new TestCertificateAuthority(request.CreateSelfSigned(DefaultNotBefore, DefaultNotAfter), key);
    }

    public TestCertificateAuthority IssueIntermediate(string name = "CN=Mtls Tests Intermediate CA")
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Certificate, true, false));

        using X509Certificate2 issued = request.Create(Certificate, DefaultNotBefore.AddHours(1), DefaultNotAfter.AddHours(-1), RandomNumberGenerator.GetBytes(16));
        return new TestCertificateAuthority(issued.CopyWithPrivateKey(key), key);
    }

    public X509Certificate2 IssueLeaf(
        string subject = "CN=client-one",
        string[]? extendedKeyUsages = null,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, leafKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));

        string[] usages = extendedKeyUsages ?? [ClientAuthenticationOid];
        if (usages.Length > 0)
        {
            var oids = new OidCollection();
            foreach (string usage in usages)
            {
                oids.Add(new Oid(usage));
            }

            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, false));
        }

        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Certificate, true, false));

        using X509Certificate2 issued = request.Create(
            Certificate,
            notBefore ?? DefaultNotBefore.AddDays(1),
            notAfter ?? DefaultNotAfter.AddDays(-1),
            RandomNumberGenerator.GetBytes(16));
        return issued.CopyWithPrivateKey(leafKey);
    }

    public void Dispose() => _key.Dispose();
}
