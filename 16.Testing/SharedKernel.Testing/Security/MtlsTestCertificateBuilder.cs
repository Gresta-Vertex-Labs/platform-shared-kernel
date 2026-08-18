using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SharedKernel.Testing.Security;

/// <summary>
/// Fluent builder that constructs a self-signed or CA-chained X.509 test certificate for use as an
/// input fixture in a consuming service's own mTLS integration test.
/// </summary>
/// <remarks>
/// <para>
/// Built entirely on BCL <see cref="System.Security.Cryptography.X509Certificates"/>
/// (<see cref="CertificateRequest"/>, <see cref="X509Certificate2"/>,
/// <see cref="CertificateRevocationListBuilder"/>) — zero <c>SharedKernel.Security.Mtls</c> reference.
/// <c>IMtlsCertificateValidator</c>/<c>MtlsAuthenticationOptions</c> are CONSUMED by a test using this
/// builder's output, never faked BY this builder.
/// </para>
/// <para>
/// DOCUMENTED SIMPLIFICATION: this builder cannot fabricate a live, network-reachable CRL Distribution
/// Point a real <see cref="X509Chain"/>.<c>Build()</c> would fetch from automatically — a consuming test
/// must feed <see cref="MtlsTestCertificate.RevocationList"/> into its own
/// <c>X509Chain.ChainPolicy</c> (<c>ExtraStore</c>/a local CRL cache seed) manually.
/// <c>X509RevocationMode.Online</c>/automatic-offline-cache-population fetch behavior is NOT reproduced.
/// </para>
/// <para>
/// Each generated ECDsa P-256 key pair (leaf and, for the chained modes, the ephemeral CA) and the
/// leaf certificate's random serial number are inherent cryptographic material — not a violation of
/// this package's determinism convention, which governs test-assertion-relevant defaults; the validity
/// window below defaults to a FIXED, non-real window, never real <see cref="DateTimeOffset.UtcNow"/>.
/// </para>
/// </remarks>
public sealed class MtlsTestCertificateBuilder
{
    private const string DefaultSubjectName = "CN=mtls-test-client";
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    private static readonly DateTimeOffset DefaultNotBefore = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DefaultNotAfter = new(2034, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private string _subjectName = DefaultSubjectName;
    private DateTimeOffset _notBefore = DefaultNotBefore;
    private DateTimeOffset _notAfter = DefaultNotAfter;
    private CertificateMode _mode = CertificateMode.SelfSigned;

    /// <summary>Sets the certificate's subject distinguished name. Defaults to <c>"CN=mtls-test-client"</c>.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public MtlsTestCertificateBuilder WithSubjectName(string subjectName = DefaultSubjectName)
    {
        ArgumentNullException.ThrowIfNull(subjectName);
        _subjectName = subjectName;
        return this;
    }

    /// <summary>
    /// Sets the certificate's validity window. Defaults to a FIXED, non-real window — never real
    /// <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public MtlsTestCertificateBuilder WithValidityPeriod(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        _notBefore = notBefore;
        _notAfter = notAfter;
        return this;
    }

    /// <summary>
    /// Configures this builder to issue a self-signed certificate — the DEFAULT mode. This is the shape
    /// a corrected <c>AllowedCertificateTypes = Chained</c> default must reject.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public MtlsTestCertificateBuilder AsSelfSigned()
    {
        _mode = CertificateMode.SelfSigned;
        return this;
    }

    /// <summary>
    /// Configures this builder to issue a leaf certificate from a freshly generated in-memory root CA —
    /// the shape an explicit chained-certificate opt-in path must still accept.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public MtlsTestCertificateBuilder AsChainedFromEphemeralCa()
    {
        _mode = CertificateMode.ChainedFromEphemeralCa;
        return this;
    }

    /// <summary>
    /// Configures this builder to issue a CA-chained leaf certificate (chaining from an ephemeral CA by
    /// necessity) and additionally records the issued certificate's serial number into that CA's
    /// revocation list.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public MtlsTestCertificateBuilder AsRevoked()
    {
        _mode = CertificateMode.Revoked;
        return this;
    }

    /// <summary>Builds the configured <see cref="MtlsTestCertificate"/>.</summary>
    public MtlsTestCertificate Build() =>
        _mode switch
        {
            CertificateMode.SelfSigned => BuildSelfSigned(),
            CertificateMode.ChainedFromEphemeralCa => BuildChained(revoked: false),
            CertificateMode.Revoked => BuildChained(revoked: true),
            _ => throw new InvalidOperationException($"Unknown certificate mode '{_mode}'."),
        };

    private MtlsTestCertificate BuildSelfSigned()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(_subjectName, key, HashAlgorithmName.SHA256);
        AddLeafExtensions(request);

        var certificate = request.CreateSelfSigned(_notBefore, _notAfter);

        return new MtlsTestCertificate(certificate, IssuingCertificate: null, RevocationList: null, IsSelfSigned: true);
    }

    private MtlsTestCertificate BuildChained(bool revoked)
    {
        using var ca = MtlsTestCertificateAuthority.CreateEphemeral(_notBefore, _notAfter);
        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var leafCertificate = ca.IssueLeafCertificate(
            _subjectName,
            leafKey,
            _notBefore,
            _notAfter,
            out var serialNumber);

        byte[]? revocationList = null;
        if (revoked)
        {
            ca.Revoke(serialNumber);
            revocationList = ca.BuildRevocationList(_notAfter);
        }

        return new MtlsTestCertificate(leafCertificate, ca.Certificate, revocationList, IsSelfSigned: false);
    }

    private static void AddLeafExtensions(CertificateRequest request)
    {
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid(ClientAuthenticationOid, "Client Authentication")],
                critical: false));
    }

    private enum CertificateMode
    {
        SelfSigned,
        ChainedFromEphemeralCa,
        Revoked,
    }

    /// <summary>
    /// The ephemeral, in-memory root CA that <see cref="AsChainedFromEphemeralCa"/>/<see cref="AsRevoked"/>
    /// generate and issue leaf certificates against.
    /// </summary>
    private sealed class MtlsTestCertificateAuthority : IDisposable
    {
        private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
        private const string SubjectName = "CN=SharedKernel Testing Ephemeral Root CA";

        private readonly ECDsa _privateKey;
        private readonly List<byte[]> _revokedSerialNumbers = [];

        private MtlsTestCertificateAuthority(X509Certificate2 certificate, ECDsa privateKey)
        {
            Certificate = certificate;
            _privateKey = privateKey;
        }

        /// <summary>Gets the CA's own certificate (private key attached, for CRL signing).</summary>
        public X509Certificate2 Certificate { get; }

        public static MtlsTestCertificateAuthority CreateEphemeral(DateTimeOffset notBefore, DateTimeOffset notAfter)
        {
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(SubjectName, key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(
                    certificateAuthority: true,
                    hasPathLengthConstraint: false,
                    pathLengthConstraint: 0,
                    critical: true));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                    critical: true));
            // Required for X509AuthorityKeyIdentifierExtension.CreateFromCertificate(includeKeyIdentifier: true)
            // in BuildRevocationList below — CRL signing needs the CA's own Subject Key Identifier present.
            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

            var certificate = request.CreateSelfSigned(notBefore, notAfter);
            return new MtlsTestCertificateAuthority(certificate, key);
        }

        public X509Certificate2 IssueLeafCertificate(
            string subjectName,
            ECDsa leafKey,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter,
            out byte[] serialNumber)
        {
            var request = new CertificateRequest(subjectName, leafKey, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(
                    certificateAuthority: false,
                    hasPathLengthConstraint: false,
                    pathLengthConstraint: 0,
                    critical: false));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: false));
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    [new Oid(ClientAuthenticationOid, "Client Authentication")],
                    critical: false));

            serialNumber = RandomNumberGenerator.GetBytes(16);

            using var leafPublicOnly = request.Create(Certificate, notBefore, notAfter, serialNumber);
            return leafPublicOnly.CopyWithPrivateKey(leafKey);
        }

        public void Revoke(byte[] serialNumber) => _revokedSerialNumbers.Add(serialNumber);

        public byte[] BuildRevocationList(DateTimeOffset nextUpdate)
        {
            var builder = new CertificateRevocationListBuilder();
            foreach (var serial in _revokedSerialNumbers)
            {
                builder.AddEntry(serial);
            }

            var generator = X509SignatureGenerator.CreateForECDsa(_privateKey);
            var authorityKeyIdentifier = X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                Certificate,
                includeKeyIdentifier: true,
                includeIssuerAndSerial: true);

            return builder.Build(
                Certificate.SubjectName,
                generator,
                crlNumber: new BigInteger(_revokedSerialNumbers.Count),
                nextUpdate,
                HashAlgorithmName.SHA256,
                authorityKeyIdentifier);
        }

        /// <remarks>
        /// Disposes only the CA's private key wrapper — <see cref="Certificate"/>'s ownership passes to
        /// every <see cref="MtlsTestCertificate.IssuingCertificate"/> this authority issued against, so
        /// it is never disposed here.
        /// </remarks>
        public void Dispose() => _privateKey.Dispose();
    }
}

/// <summary>Return type of <see cref="MtlsTestCertificateBuilder.Build"/>.</summary>
/// <param name="Certificate">The issued leaf (or self-signed) certificate, private key included.</param>
/// <param name="IssuingCertificate">
/// The ephemeral CA certificate for <c>AsChainedFromEphemeralCa()</c>/<c>AsRevoked()</c>;
/// <see langword="null"/> for a self-signed certificate.
/// </param>
/// <param name="RevocationList">
/// DER-encoded CRL bytes, populated only when <c>AsRevoked()</c> was used, listing the returned
/// certificate's serial number; <see langword="null"/> otherwise.
/// </param>
/// <param name="IsSelfSigned">Whether <see cref="Certificate"/> is self-signed.</param>
public sealed record MtlsTestCertificate(
    X509Certificate2 Certificate,
    X509Certificate2? IssuingCertificate,
    byte[]? RevocationList,
    bool IsSelfSigned);
