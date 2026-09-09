using System.Formats.Asn1;
using System.Numerics;
using System.Reflection;
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

    /// <summary>
    /// Regression test for a confirmed bug (2026-09-09): <c>AsRevoked()</c> used to feed the
    /// pre-issuance raw random serial bytes directly into <c>CertificateRevocationListBuilder.AddEntry</c>,
    /// which throws <see cref="ArgumentException"/> on ANY leading zero byte. A raw 16-byte random draw
    /// hits that shape with probability 1/256 — an intermittent ~0.4% CI failure. This test does NOT
    /// rely on a lucky (or unlucky) random draw: it drives the builder's private
    /// <c>MtlsTestCertificateAuthority.Revoke</c>/<c>BuildRevocationList</c> members via reflection
    /// with a DELIBERATELY crafted serial number whose big-endian form starts with <c>0x00</c> every
    /// single run, so it fails against the old code 100% of the time and passes against the fix 100%
    /// of the time — never a matter of luck either way.
    /// </summary>
    /// <remarks>
    /// Reflection is used here (rather than widening any production member's visibility) because
    /// <c>MtlsTestCertificateAuthority</c> is a deliberately private nested implementation detail with
    /// no public seam to inject a specific serial number through — mirroring this package's existing
    /// precedent of reaching otherwise-unexposed internals from a test rather than loosening a
    /// production access modifier purely to make a test possible.
    /// </remarks>
    [Fact]
    public void AsRevoked_SerialWithLeadingZeroByteAfterReversal_DoesNotThrow_AndCrlListsTheExactSameValue()
    {
        var notBefore = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var notAfter = new DateTimeOffset(2034, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var authorityType = typeof(MtlsTestCertificateBuilder).GetNestedType(
            "MtlsTestCertificateAuthority",
            BindingFlags.NonPublic);
        Assert.NotNull(authorityType);

        var createEphemeral = authorityType!.GetMethod("CreateEphemeral", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(createEphemeral);

        using var authority = (IDisposable)createEphemeral!.Invoke(null, [notBefore, notAfter])!;

        // Deliberately crafted, NOT random: little-endian bytes whose big-endian reversal is
        // 00 0F 0E 0D 0C 0B 0A 09 08 07 06 05 04 03 02 01 — a leading 0x00 every single time, the
        // exact shape that made CertificateRevocationListBuilder.AddEntry throw ArgumentException.
        byte[] craftedSerialLittleEndian =
            [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x00];

        var revoke = authorityType.GetMethod("Revoke", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(revoke);
        revoke!.Invoke(authority, [craftedSerialLittleEndian]);

        var buildRevocationList = authorityType.GetMethod("BuildRevocationList", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(buildRevocationList);

        byte[]? crlBytes = null;
        var exception = Record.Exception(() => crlBytes = (byte[])buildRevocationList!.Invoke(authority, [notAfter])!);

        Assert.Null(exception);
        Assert.NotNull(crlBytes);

        // Independent correctness check — computed WITHOUT reusing the production transform, so a
        // subtle bug in that transform could not make this assertion pass vacuously: read the CRL's
        // one entry's raw content bytes and confirm they represent the exact same UNSIGNED numeric
        // value as the crafted serial (never a signed BigInteger conversion — AddEntry can legitimately
        // produce content bytes that read as negative under strict signed DER semantics; see this
        // class's own CrlContainsSerialNumber remarks).
        var expectedValue = new BigInteger(craftedSerialLittleEndian, isUnsigned: true, isBigEndian: false);
        var entryContentBytes = ReadFirstRevokedEntrySerialContentBytes(crlBytes!);
        var actualValue = new BigInteger(entryContentBytes, isUnsigned: true, isBigEndian: true);

        Assert.Equal(expectedValue, actualValue);
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

    /// <summary>
    /// Parses DER-encoded CRL bytes by hand and returns the first (and, in this test's usage, only)
    /// revoked entry's raw <c>INTEGER</c> content bytes — the same navigation
    /// <see cref="CrlContainsSerialNumber"/> performs, extracted standalone so a caller can inspect the
    /// value independently rather than only comparing it against one expected byte pattern.
    /// </summary>
    private static byte[] ReadFirstRevokedEntrySerialContentBytes(byte[] derCrl)
    {
        var outer = new AsnReader(derCrl, AsnEncodingRules.DER);
        var certificateList = outer.ReadSequence();
        var tbsCertList = certificateList.ReadSequence();

        tbsCertList.ReadInteger(); // version
        tbsCertList.ReadSequence(); // signature AlgorithmIdentifier
        tbsCertList.ReadSequence(); // issuer Name
        SkipTime(tbsCertList); // thisUpdate
        SkipTime(tbsCertList); // nextUpdate

        var revokedCertificates = tbsCertList.ReadSequence();
        var entry = revokedCertificates.ReadSequence();
        return ReadIntegerContentBytes(entry);
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
