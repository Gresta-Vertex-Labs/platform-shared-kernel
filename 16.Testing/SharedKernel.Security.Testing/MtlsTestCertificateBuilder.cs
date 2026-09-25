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
/// <para>
/// FIXED (2026-09-09): <c>AsRevoked()</c> previously fed the pre-issuance raw random serial bytes
/// directly into both <c>CertificateRevocationListBuilder.AddEntry</c> and the CRL's own revoked-entry
/// list. That crashed with <see cref="ArgumentException"/> roughly 1-in-256 runs (whenever the random
/// draw's leading byte, once reversed to big-endian, was <c>0x00</c>) and, independent of the crash,
/// could disagree with the certificate's actual embedded serial whenever
/// <see cref="CertificateRequest.Create(X509Certificate2, DateTimeOffset, DateTimeOffset, byte[])"/>
/// re-normalized the input during embedding. The fix reads the AUTHORITATIVE serial back from the
/// issued certificate itself (<c>X509Certificate2.GetSerialNumber</c>) and converts it to the
/// big-endian, zero-leading-byte-free form <c>AddEntry</c> requires
/// (<see cref="MtlsTestCertificateAuthority.ToCrlEntrySerialNumber"/>) — see that method's remarks for
/// the full mechanics, confirmed via a throwaway smoke-test program per this package's crypto-fixture
/// verification discipline.
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

            var rawSerialNumber = RandomNumberGenerator.GetBytes(16);

            using var leafPublicOnly = request.Create(Certificate, notBefore, notAfter, rawSerialNumber);
            var leaf = leafPublicOnly.CopyWithPrivateKey(leafKey);

            // CertificateRequest.Create does NOT necessarily embed rawSerialNumber byte-for-byte: it
            // treats the input as a big-endian magnitude and re-normalizes it (stripping genuinely
            // redundant leading zero bytes, but keeping exactly one 0x00 sign-safety pad when the
            // normalized leading byte's high bit is set) before DER-encoding it into the certificate.
            // Read the ACTUAL embedded serial back from the issued certificate itself — never return
            // the pre-issuance raw draw — so a later CRL entry is built from, and therefore guaranteed
            // to agree with, the exact serial the certificate really carries.
            serialNumber = leaf.GetSerialNumber();
            return leaf;
        }

        public void Revoke(byte[] serialNumber) => _revokedSerialNumbers.Add(serialNumber);

        public byte[] BuildRevocationList(DateTimeOffset nextUpdate)
        {
            var builder = new CertificateRevocationListBuilder();
            foreach (var serial in _revokedSerialNumbers)
            {
                builder.AddEntry(ToCrlEntrySerialNumber(serial));
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

        /// <summary>
        /// Converts a serial number from the little-endian form <c>X509Certificate2.GetSerialNumber</c>
        /// returns into the big-endian, zero-leading-byte-free form <c>CertificateRevocationListBuilder.AddEntry(byte[], DateTimeOffset?)</c>
        /// requires.
        /// </summary>
        /// <remarks>
        /// <para>
        /// CONFIRMED VIA A THROWAWAY SMOKE-TEST PROGRAM (never trust this shape from memory or docs
        /// alone): <c>CertificateRevocationListBuilder.AddEntry(byte[], DateTimeOffset?)</c>
        /// throws <see cref="ArgumentException"/> on ANY leading <c>0x00</c> byte — including the
        /// legitimate DER sign-safety pad byte that <c>X509Certificate2.GetSerialNumber</c>
        /// itself carries when the serial's most-significant byte has its high bit set — and it never
        /// re-applies that pad on its own. A raw 16-byte random serial starts with a redundant leading
        /// zero (once reversed to big-endian) with probability 1/256, which is exactly the
        /// intermittent <c>~1-in-256</c> CI failure this method fixes.
        /// </para>
        /// <para>
        /// Stripping the pad byte here means a serial whose magnitude's top bit is set is encoded by
        /// <c>CertificateRevocationListBuilder.AddEntry(byte[], DateTimeOffset?)</c> as a
        /// technically-negative DER <c>INTEGER</c> under strict signed ASN.1 semantics, even though it
        /// represents an always-positive serial number. This is a confirmed, unavoidable limitation of
        /// <c>CertificateRevocationListBuilder.AddEntry(byte[], DateTimeOffset?)</c> itself, not
        /// a defect introduced here — see <c>MtlsTestCertificateBuilderTests.CrlContainsSerialNumber</c>,
        /// which already reads the entry's raw content bytes directly rather than converting through a
        /// signed <see cref="System.Numerics.BigInteger"/> for exactly this reason.
        /// </para>
        /// </remarks>
        private static byte[] ToCrlEntrySerialNumber(byte[] serialNumberLittleEndian)
        {
            var bigEndian = (byte[])serialNumberLittleEndian.Clone();
            Array.Reverse(bigEndian);

            var firstNonZero = 0;
            while (firstNonZero < bigEndian.Length - 1 && bigEndian[firstNonZero] == 0x00)
            {
                firstNonZero++;
            }

            return firstNonZero == 0 ? bigEndian : bigEndian[firstNonZero..];
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
