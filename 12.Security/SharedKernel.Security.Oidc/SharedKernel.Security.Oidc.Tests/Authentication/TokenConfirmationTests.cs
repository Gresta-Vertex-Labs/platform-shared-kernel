using System.Security.Claims;
using SharedKernel.Security.Oidc.Authentication;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Authentication;

public sealed class TokenConfirmationTests
{
    [Fact]
    public void Read_NoConfirmation_IsUnconstrained()
    {
        TokenConfirmation confirmation = TokenConfirmation.Read(Identity());

        Assert.False(confirmation.IsSenderConstrained);
        Assert.False(confirmation.IsMalformed);
    }

    [Fact]
    public void Read_JwkThumbprint_IsReturned()
    {
        TokenConfirmation confirmation = TokenConfirmation.Read(Identity("{\"jkt\":\"abc\"}"));

        Assert.Equal("abc", confirmation.JwkThumbprint);
        Assert.Null(confirmation.CertificateThumbprint);
        Assert.True(confirmation.IsSenderConstrained);
        Assert.False(confirmation.IsMalformed);
    }

    [Fact]
    public void Read_BothThumbprints_AreReturned()
    {
        TokenConfirmation confirmation = TokenConfirmation.Read(Identity("{\"jkt\":\"abc\",\"x5t#S256\":\"def\",\"other\":1}"));

        Assert.Equal("abc", confirmation.JwkThumbprint);
        Assert.Equal("def", confirmation.CertificateThumbprint);
        Assert.False(confirmation.IsMalformed);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[\"jkt\"]")]
    [InlineData("\"jkt\"")]
    [InlineData("{\"jkt\":42}")]
    [InlineData("{\"jkt\":\"\"}")]
    [InlineData("{\"jkt\":null}")]
    [InlineData("{\"x5t#S256\":{\"nested\":true}}")]
    public void Read_InvalidConfirmation_IsMalformed(string value)
    {
        TokenConfirmation confirmation = TokenConfirmation.Read(Identity(value));

        Assert.True(confirmation.IsMalformed);
        Assert.False(confirmation.IsSenderConstrained);
    }

    [Fact]
    public void Read_MultipleConfirmationClaims_IsMalformed()
    {
        TokenConfirmation confirmation = TokenConfirmation.Read(Identity("{\"jkt\":\"a\"}", "{\"jkt\":\"b\"}"));

        Assert.True(confirmation.IsMalformed);
    }

    private static ClaimsIdentity Identity(params string[] confirmations) =>
        new([new Claim("sub", "user-1"), .. confirmations.Select(value => new Claim("cnf", value))], "Bearer");
}
