using System.Text;
using SharedKernel.Cryptography.Totp;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Totp;

/// <summary>
/// Covers <see cref="TotpProvisioningUri"/> (C-70/T-56) — the <c>otpauth://totp/...</c> Key Uri
/// Format builder, verified field-for-field against the documented format.
/// </summary>
public sealed class TotpProvisioningUriTests
{
    /// <summary>Minimal hand-rolled query-string parser — avoids pulling in System.Web/ASP.NET just to assert on a query string in a test.</summary>
    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>();
        string trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
        {
            return result;
        }

        foreach (string pair in trimmed.Split('&'))
        {
            string[] parts = pair.Split('=', 2);
            result[Uri.UnescapeDataString(parts[0])] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }

        return result;
    }

    [Fact]
    public void Build_MatchesKeyUriFormatFieldForField()
    {
        byte[] secret = Encoding.ASCII.GetBytes("12345678901234567890");
        string expectedSecret = Base32.Encode(secret);

        Uri uri = TotpProvisioningUri.Build("Contoso", "alice@example.com", secret, digits: 6, stepSeconds: 30, algorithm: HotpAlgorithm.Sha1);

        Assert.Equal("otpauth", uri.Scheme);
        Assert.Equal("totp", uri.Host);
        Assert.Equal("/Contoso:alice@example.com", Uri.UnescapeDataString(uri.AbsolutePath));

        Dictionary<string, string> query = ParseQuery(uri.Query);

        Assert.Equal(expectedSecret, query["secret"]);
        Assert.Equal("Contoso", query["issuer"]);
        Assert.Equal("6", query["digits"]);
        Assert.Equal("30", query["period"]);
        Assert.Equal("SHA1", query["algorithm"]);
    }

    [Theory]
    [InlineData(HotpAlgorithm.Sha1, "SHA1")]
    [InlineData(HotpAlgorithm.Sha256, "SHA256")]
    [InlineData(HotpAlgorithm.Sha512, "SHA512")]
    public void Build_AlgorithmNameMatchesKeyUriFormat(HotpAlgorithm algorithm, string expectedName)
    {
        byte[] secret = [1, 2, 3, 4, 5];

        Uri uri = TotpProvisioningUri.Build("Issuer", "user", secret, algorithm: algorithm);

        Assert.Contains($"algorithm={expectedName}", uri.Query);
    }

    [Fact]
    public void Build_EncodesSpecialCharactersInIssuerAndAccount()
    {
        byte[] secret = [1, 2, 3, 4, 5];

        Uri uri = TotpProvisioningUri.Build("My Company", "user name@example.com", secret);

        Assert.DoesNotContain(" ", uri.AbsoluteUri.Replace("%20", string.Empty));
        Assert.Contains("My%20Company", uri.AbsoluteUri);
    }

    [Fact]
    public void Build_NullSecret_Throws() =>
        Assert.Throws<ArgumentNullException>(() => TotpProvisioningUri.Build("Issuer", "account", null!));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_InvalidIssuer_Throws(string invalidIssuer) =>
        Assert.Throws<ArgumentException>(() => TotpProvisioningUri.Build(invalidIssuer, "account", [1, 2, 3]));
}
