using System.Text;
using System.Web;
using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Cryptography.Tests.Totp;

public sealed class TotpProvisioningUriTests
{
    private static readonly byte[] Secret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Fact]
    public void Build_DefaultParameters_WritesEveryQueryParameter()
    {
        Uri uri = TotpProvisioningUri.Build("Contoso", "alice@example.com", Secret);

        Assert.Equal("otpauth", uri.Scheme);
        Assert.Equal("totp", uri.Host);
        Assert.Equal(
            "otpauth://totp/Contoso:alice%40example.com?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&issuer=Contoso&algorithm=SHA1&digits=6&period=30",
            uri.OriginalString);
    }

    [Fact]
    public void Build_SecretIsBase32EncodedWithoutPadding()
    {
        Uri uri = TotpProvisioningUri.Build("Contoso", "alice", Secret);

        string secret = HttpUtility.ParseQueryString(uri.Query)["secret"]!;
        Assert.DoesNotContain('=', secret);
        Assert.Equal(Secret, Base32.Decode(secret).Value);
    }

    [Theory]
    [InlineData(HotpAlgorithm.Sha1, "SHA1")]
    [InlineData(HotpAlgorithm.Sha256, "SHA256")]
    [InlineData(HotpAlgorithm.Sha512, "SHA512")]
    public void Build_Algorithm_IsWrittenByName(HotpAlgorithm algorithm, string expected)
    {
        Uri uri = TotpProvisioningUri.Build("Contoso", "alice", Secret, new TotpParameters { Algorithm = algorithm });

        Assert.Equal(expected, HttpUtility.ParseQueryString(uri.Query)["algorithm"]);
    }

    [Fact]
    public void Build_CustomParameters_WritesDigitsAndPeriod()
    {
        Uri uri = TotpProvisioningUri.Build("Contoso", "alice", Secret, new TotpParameters { Digits = 8, StepSeconds = 60 });

        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("8", query["digits"]);
        Assert.Equal("60", query["period"]);
    }

    [Fact]
    public void Build_EscapesIssuerAndAccountName()
    {
        Uri uri = TotpProvisioningUri.Build("A&B Co/Ltd", "ali ce+tag@example.com?x=1", Secret);

        Assert.StartsWith("otpauth://totp/A%26B%20Co%2FLtd:ali%20ce%2Btag%40example.com%3Fx%3D1?", uri.OriginalString, StringComparison.Ordinal);
        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("A&B Co/Ltd", query["issuer"]);
        Assert.Equal(5, query.Count);
    }

    [Fact]
    public void Build_NonAsciiIssuer_IsPercentEncoded()
    {
        Uri uri = TotpProvisioningUri.Build("Şirket", "kullanıcı", Secret);

        Assert.StartsWith("otpauth://totp/%C5%9Eirket:kullan%C4%B1c%C4%B1?", uri.OriginalString, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Con:toso", "alice")]
    [InlineData("Contoso", "alice:admin")]
    [InlineData("", "alice")]
    [InlineData("Contoso", " ")]
    public void Build_InvalidLabelPart_Throws(string issuer, string accountName)
    {
        Assert.Throws<ArgumentException>(() => TotpProvisioningUri.Build(issuer, accountName, Secret));
    }

    [Fact]
    public void Build_NullLabelPart_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TotpProvisioningUri.Build(null!, "alice", Secret));
        Assert.Throws<ArgumentNullException>(() => TotpProvisioningUri.Build("Contoso", null!, Secret));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void Build_SecretShorterThan16Bytes_Throws(int length)
    {
        Assert.Throws<ArgumentException>(() => TotpProvisioningUri.Build("Contoso", "alice", new byte[length]));
    }
}
