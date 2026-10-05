using Xunit;

namespace SharedKernel.Security.Abstractions.Tests;

public sealed class SecurityClaimTypesTests
{
    [Theory]
    [InlineData(SecurityClaimTypes.Subject, "sub")]
    [InlineData(SecurityClaimTypes.TokenId, "jti")]
    [InlineData(SecurityClaimTypes.Name, "name")]
    [InlineData(SecurityClaimTypes.Email, "email")]
    [InlineData(SecurityClaimTypes.AuthenticationMethod, "amr")]
    [InlineData(SecurityClaimTypes.AuthContextClassReference, "acr")]
    [InlineData(SecurityClaimTypes.AuthTime, "auth_time")]
    [InlineData(SecurityClaimTypes.SessionId, "sid")]
    [InlineData(SecurityClaimTypes.AuthorizedParty, "azp")]
    [InlineData(SecurityClaimTypes.Scope, "scope")]
    [InlineData(SecurityClaimTypes.ClientId, "client_id")]
    [InlineData(SecurityClaimTypes.Confirmation, "cnf")]
    [InlineData(SecurityClaimTypes.Roles, "roles")]
    [InlineData(SecurityClaimTypes.TenantId, "tenant_id")]
    [InlineData(SecurityClaimTypes.AuthenticationMethodTime, "amr_time")]
    public void Constant_MatchesRegisteredWireName(string actual, string expected)
    {
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Constants_AreDistinct()
    {
        string[] values = typeof(SecurityClaimTypes)
            .GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(15, values.Length);
        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
    }
}
