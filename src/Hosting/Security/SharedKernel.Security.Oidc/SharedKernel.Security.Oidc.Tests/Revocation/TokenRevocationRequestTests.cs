using SharedKernel.Security.Oidc.Revocation;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Revocation;

public sealed class TokenRevocationRequestTests
{
    private static readonly DateTimeOffset ExpiresAt = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_SetsProperties()
    {
        var request = new TokenRevocationRequest("eyJ.secret.token", "hash", "jti", "sub", "client", "session", ExpiresAt);

        Assert.Equal("eyJ.secret.token", request.Token);
        Assert.Equal("hash", request.TokenHash);
        Assert.Equal("jti", request.TokenId);
        Assert.Equal("sub", request.SubjectId);
        Assert.Equal("client", request.ClientId);
        Assert.Equal("session", request.SessionId);
        Assert.Equal(ExpiresAt, request.ExpiresAt);
    }

    [Fact]
    public void ToString_OmitsTokenAndIncludesHash()
    {
        var request = new TokenRevocationRequest("eyJ.secret.token", "hash-value", "jti", "sub", "client", "session", ExpiresAt);

        string text = request.ToString();

        Assert.DoesNotContain("eyJ.secret.token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.Contains("hash-value", text, StringComparison.Ordinal);
        Assert.Contains("2030-01-01T00:00:00.0000000+00:00", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "hash")]
    [InlineData("", "hash")]
    [InlineData("token", null)]
    [InlineData("token", "")]
    public void Constructor_MissingTokenOrHash_Throws(string? token, string? hash)
    {
        Assert.ThrowsAny<ArgumentException>(() => new TokenRevocationRequest(token!, hash!, null, null, null, null, ExpiresAt));
    }
}
