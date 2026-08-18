using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Security;

/// <summary>
/// Proves <see cref="MtlsTestCertificateBuilder"/>'s self-signed, ephemeral-CA-chained, and revoked
/// certificate construction modes.
/// </summary>
/// <remarks>
/// The chain-validation assertion below builds a REAL <see cref="X509Chain"/> against the returned
/// <see cref="MtlsTestCertificate.IssuingCertificate"/>; the CRL assertion parses the returned
/// <see cref="MtlsTestCertificate.RevocationList"/> DER bytes by hand (via <see cref="AsnReader"/>) and
/// locates the exact revoked serial number — neither assertion trusts the builder's own internal state,
/// mirroring this domain's "prove crypto output is genuinely valid, not merely well-formed" discipline.
/// </remarks>
public sealed class MtlsTestCertificateBuilderTests
{
    [Fact]
    public void AsSelfSigned_IssuingCertificateIsNull()
    {
        var certificate = new MtlsTestCertificateBuilder().AsSelfSigned().Build();

        Assert.Null(certificate.IssuingCertificate);
        Assert.True(certificate.IsSelfSigned);
        Assert.Null(certificate.RevocationList);
    }

    [Fact]
    public void AsSelfSigned_IsTheDefaultMode()
    {
        var withoutExplicitMode = new MtlsTestCertificateBuilder().Build();

        Assert.Null(withoutExplicitMode.IssuingCertificate);
        Assert.True(withoutExplicitMode.IsSelfSigned);
    }

    [Fact]
    public void AsChainedFromEphemeralCa_IssuingCertificateIsNotNull_AndIsNotSelfSigned()
    {
        var certificate = new MtlsTestCertificateBuilder().AsChainedFromEphemeralCa().Build();

        Assert.NotNull(certificate.IssuingCertificate);
        Assert.False(certificate.IsSelfSigned);
        Assert.Null(certificate.RevocationList);
    }

    [Fact]
    public void AsChainedFromEphemeralCa_LeafValidatesAgainstReturnedIssuingCertificate_ViaRealX509Chain()
    {
        var certificate = new MtlsTestCertificateBuilder().AsChainedFromEphemeralCa().Build();

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(certificate.IssuingCertificate!);
        chain.ChainPolicy.ExtraStore.Add(certificate.IssuingCertificate!);

        var isValid = chain.Build(certificate.Certificate);

        Assert.True(isValid);
        Assert.Equal(2, chain.ChainElements.Count);
    }

    [Fact]
    public void AsChainedFromEphemeralCa_WithoutIssuingCertificateInTrustStore_ChainDoesNotValidate()
    {
        var certificate = new MtlsTestCertificateBuilder().AsChainedFromEphemeralCa().Build();

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        // Deliberately empty CustomTrustStore — proves the prior test's success is genuinely caused by
        // trusting the returned IssuingCertificate, not an artifact of ambient machine trust.

        var isValid = chain.Build(certificate.Certificate);

        Assert.False(isValid);
    }

    [Fact]
    public void AsRevoked_IssuingCertificateIsNotNull_AndRevocationListIsNotNull()
    {
        var certificate = new MtlsTestCertificateBuilder().AsRevoked().Build();

        Assert.NotNull(certificate.IssuingCertificate);
        Assert.NotNull(certificate.RevocationList);
    }

    [Fact]
    public void AsRevoked_RevocationList_ParsesAsValidDer_AndListsExactCertificateSerialNumber()
    {
        var certificate = new MtlsTestCertificateBuilder().AsRevoked().Build();

        var found = CrlContainsSerialNumber(certificate.RevocationList!, certificate.Certificate.GetSerialNumber());

        Assert.True(found);
    }

    [Fact]
    public void AsRevoked_RevocationList_DoesNotListAnUnrelatedRandomSerialNumber()
    {
        var certificate = new MtlsTestCertificateBuilder().AsRevoked().Build();
        var unrelatedSerial = RandomNumberGenerator.GetBytes(16);

        var found = CrlContainsSerialNumber(certificate.RevocationList!, unrelatedSerial);

        Assert.False(found);
    }

    [Fact]
    public void WithSubjectName_IsHonoredExactly()
    {
        var certificate = new MtlsTestCertificateBuilder().WithSubjectName("CN=custom-test-subject").Build();

        Assert.Equal("CN=custom-test-subject", certificate.Certificate.Subject);
    }

    [Fact]
    public void WithValidityPeriod_IsHonoredExactly()
    {
        var notBefore = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var notAfter = new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero);

        var certificate = new MtlsTestCertificateBuilder().WithValidityPeriod(notBefore, notAfter).Build();

        Assert.Equal(notBefore.UtcDateTime, certificate.Certificate.NotBefore.ToUniversalTime());
        Assert.Equal(notAfter.UtcDateTime, certificate.Certificate.NotAfter.ToUniversalTime());
    }

    /// <summary>
    /// Parses DER-encoded CRL bytes by hand and determines whether <paramref name="serialNumberLittleEndian"/>
    /// (in the same little-endian order <see cref="X509Certificate2.GetSerialNumber"/> returns) is listed
    /// as a revoked entry.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT use <see cref="AsnReader.ReadInteger"/>'s <see cref="System.Numerics.BigInteger"/>
    /// conversion for the entry's serial number: <see cref="CertificateRevocationListBuilder.AddEntry"/>
    /// encodes the serial's raw content bytes without re-applying the DER sign-safety zero-pad that
    /// <see cref="X509Certificate2.GetSerialNumber"/>'s own certificate-embedding path applies (confirmed
    /// via a throwaway smoke-test program), so the encoded INTEGER can be technically negative under
    /// strict signed ASN.1 semantics even though it represents a always-positive serial number. Raw content
    /// bytes are compared directly instead, sidestepping the sign question entirely.
    /// </remarks>
    private static bool CrlContainsSerialNumber(byte[] derCrl, byte[] serialNumberLittleEndian)
    {
        var trimmed = serialNumberLittleEndian;
        if (trimmed.Length > 1 && trimmed[^1] == 0x00 && (trimmed[^2] & 0x80) != 0)
        {
            // Strip the trailing DER sign-safety pad byte X509Certificate2.GetSerialNumber() adds.
            trimmed = trimmed[..^1];
        }

        var expectedBigEndian = (byte[])trimmed.Clone();
        Array.Reverse(expectedBigEndian);

        var outer = new AsnReader(derCrl, AsnEncodingRules.DER);
        var certificateList = outer.ReadSequence();
        var tbsCertList = certificateList.ReadSequence();

        tbsCertList.ReadInteger(); // version
        tbsCertList.ReadSequence(); // signature AlgorithmIdentifier
        tbsCertList.ReadSequence(); // issuer Name
        SkipTime(tbsCertList); // thisUpdate
        SkipTime(tbsCertList); // nextUpdate

        if (!tbsCertList.HasData)
        {
            return false;
        }

        var nextTag = tbsCertList.PeekTag();
        if (nextTag.TagClass != TagClass.Universal || nextTag.TagValue != (int)UniversalTagNumber.Sequence)
        {
            // No revokedCertificates SEQUENCE OF present — only crlExtensions [0] follows.
            return false;
        }

        var revokedCertificates = tbsCertList.ReadSequence();
        while (revokedCertificates.HasData)
        {
            var entry = revokedCertificates.ReadSequence();
            var serialBytes = ReadIntegerContentBytes(entry);
            if (serialBytes.AsSpan().SequenceEqual(expectedBigEndian))
            {
                return true;
            }

            while (entry.HasData)
            {
                entry.ReadEncodedValue(); // drain revocationDate / optional crlEntryExtensions
            }
        }

        return false;
    }

    private static byte[] ReadIntegerContentBytes(AsnReader reader)
    {
        var tlv = reader.ReadEncodedValue().ToArray();
        var idx = 1; // skip tag byte (0x02 INTEGER)
        int length;
        if ((tlv[idx] & 0x80) == 0)
        {
            length = tlv[idx];
            idx += 1;
        }
        else
        {
            var lengthByteCount = tlv[idx] & 0x7F;
            idx += 1;
            length = 0;
            for (var i = 0; i < lengthByteCount; i++)
            {
                length = (length << 8) | tlv[idx + i];
            }

            idx += lengthByteCount;
        }

        return tlv[idx..(idx + length)];
    }

    private static void SkipTime(AsnReader reader)
    {
        var tag = reader.PeekTag();
        if (tag.TagClass == TagClass.Universal &&
            (tag.TagValue == (int)UniversalTagNumber.UtcTime || tag.TagValue == (int)UniversalTagNumber.GeneralizedTime))
        {
            reader.ReadEncodedValue();
            return;
        }

        throw new InvalidOperationException($"Expected an ASN.1 Time (UTCTime/GeneralizedTime), got tag {tag}.");
    }
}
