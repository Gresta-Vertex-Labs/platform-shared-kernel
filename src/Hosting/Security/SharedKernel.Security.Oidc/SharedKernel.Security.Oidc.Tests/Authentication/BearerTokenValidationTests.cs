using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.Security.Oidc.Tests.Infrastructure;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Authentication;

public sealed class BearerTokenValidationTests
{
    [Fact]
    public async Task Authenticate_TokenWithShortClaimNames_PopulatesUserContext()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        Guid tenantId = Guid.NewGuid();
        string token = TestTokens.Create()
            .WithClaim("sub", "user-42")
            .WithClaim("roles", new[] { "admin", "approver" })
            .WithClaim("email", "ada@example.test")
            .WithClaim("name", "Ada Lovelace")
            .WithClaim("scope", "orders.read orders.write")
            .WithClaim("scp", "orders.approve")
            .WithClaim("amr", new[] { "pwd", "mfa" })
            .WithClaim("acr", "urn:loa:high")
            .WithClaim("auth_time", 1_700_000_000L)
            .WithClaim("sid", "session-7")
            .WithClaim("azp", "spa-client")
            .WithClaim("tenant_id", tenantId.ToString())
            .Build();

        UserResponse user = await host.GetUserAsync(token);

        Assert.Equal("User", user.Kind);
        Assert.True(user.IsAuthenticated);
        Assert.Equal("user-42", user.SubjectId);
        Assert.Equal("spa-client", user.ClientId);
        Assert.Equal(["admin", "approver"], user.Roles);
        Assert.Equal("ada@example.test", user.Email);
        Assert.Equal("Ada Lovelace", user.Name);
        Assert.Equal(["orders.read", "orders.write", "orders.approve"], user.Permissions);
        Assert.Equal(["pwd", "mfa"], user.AuthenticationMethods);
        Assert.Equal("urn:loa:high", user.AuthContextClassReference);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), user.AuthTime);
        Assert.Equal("session-7", user.SessionId);
        Assert.Equal(tenantId, user.TenantId);
        Assert.False(user.IsSenderConstrained);
    }

    [Fact]
    public async Task Authenticate_ValidToken_KeepsIssuedClaimNamesOnPrincipal()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create()
            .WithClaim("name", "Ada")
            .WithClaim("roles", "admin")
            .WithClaim("email", "ada@example.test")
            .Build();

        UserResponse user = await host.GetUserAsync(token);

        Assert.Contains("sub", user.ClaimTypes);
        Assert.Contains("roles", user.ClaimTypes);
        Assert.Contains("email", user.ClaimTypes);
        Assert.False(user.HasMappedNameIdentifier);
        Assert.DoesNotContain(user.ClaimTypes, type => type.StartsWith("http://", StringComparison.Ordinal));
        Assert.Equal("Ada", user.PrincipalName);
    }

    [Fact]
    public async Task Authenticate_SingleRoleString_MapsOneRole()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create().WithClaim("roles", "admin").Build();

        UserResponse user = await host.GetUserAsync(token);

        Assert.Equal(["admin"], user.Roles);
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("auditor", true)]
    [InlineData("root", false)]
    public async Task IsInRole_RolesClaim_MatchesPrincipalRoles(string role, bool expected)
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create().WithClaim("roles", new[] { "admin", "auditor" }).Build();

        using HttpResponseMessage response = await host.SendAsync(token, path: $"/roles/{role}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, JsonSerializer.Deserialize<bool>(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Authenticate_EcSignedToken_Succeeds()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create().SignedWith(TestTokens.EcKey, SecurityAlgorithms.EcdsaSha256).Build();

        UserResponse user = await host.GetUserAsync(token);

        Assert.Equal("user-1", user.SubjectId);
    }

    [Fact]
    public async Task Authenticate_Ps256SignedToken_SucceedsByDefault()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create().SignedWith(TestTokens.RsaKey, SecurityAlgorithms.RsaSsaPssSha256).Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_NoToken_Returns401()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();

        using HttpResponseMessage response = await host.SendAsync(token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_WrongAudience_Returns401()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create().WithAudience("api://payments").Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_WrongIssuer_Returns401()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create().WithIssuer("https://attacker.example.test").Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_ExpiredToken_Returns401()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create()
            .WithLifetime(DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddMinutes(-10))
            .Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_ExpiredWithinClockSkew_Succeeds()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create()
            .WithLifetime(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddSeconds(-10))
            .Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_UnsignedAlgNoneToken_Returns401()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Unsigned(TestTokens.StandardPayload());

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_Hs256TokenWithKnownKey_Returns401()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create().SignedWith(TestTokens.SymmetricKey, SecurityAlgorithms.HmacSha256).Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Control for the test above: the symmetric key really validates the token once the allow-list admits HS256,
    // so the 401 there comes from the allow-list and not from a missing key.
    [Fact]
    public async Task StartAsync_Hs256AllowListOverriddenAfterPackage_FailsStartup()
    {
        OptionsValidationException exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            OidcTestHost.StartAsync(options => options.AfterOidc = services =>
                services.PostConfigure<JwtBearerOptions>(
                    OidcAuthenticationDefaults.AuthenticationScheme,
                    jwt => jwt.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256])));

        Assert.Contains("ValidAlgorithms", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_ValidationWeakenedAfterPackage_FailsStartupNamingEachSetting()
    {
        OptionsValidationException exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            OidcTestHost.StartAsync(options => options.AfterOidc = services =>
                services.PostConfigure<JwtBearerOptions>(
                    OidcAuthenticationDefaults.AuthenticationScheme,
                    jwt =>
                    {
                        jwt.MapInboundClaims = true;
                        jwt.TokenValidationParameters.ValidateLifetime = false;
                        jwt.TokenValidationParameters.RequireSignedTokens = false;
                    })));

        Assert.Contains("MapInboundClaims", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ValidateLifetime", exception.Message, StringComparison.Ordinal);
        Assert.Contains("RequireSignedTokens", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_IssuerValidatorConfiguredAfterPackage_Starts()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync(options => options.AfterOidc = services =>
            services.Configure<JwtBearerOptions>(
                OidcAuthenticationDefaults.AuthenticationScheme,
                jwt => jwt.TokenValidationParameters.IssuerValidator = (issuer, _, _) => issuer));

        Assert.NotNull(host);
    }

    [Fact]
    public async Task Authenticate_DisallowedAlgorithm_LogsAlgorithmRejection()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create().SignedWith(TestTokens.RsaKey, SecurityAlgorithms.RsaSha384).Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12104), "Algorithm", "RS384");
    }

    [Fact]
    public async Task Authenticate_Rs384WhenNotConfigured_Returns401()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create().SignedWith(TestTokens.RsaKey, SecurityAlgorithms.RsaSha384).Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_Rs384WhenConfigured_Succeeds()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
            options.Settings["SharedKernel:Security:Oidc:ValidAlgorithms:0"] = "RS384");
        string token = TestTokens.Create().SignedWith(TestTokens.RsaKey, SecurityAlgorithms.RsaSha384).Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(SecurityAlgorithms.RsaSha256, HttpStatusCode.Unauthorized)]
    [InlineData(SecurityAlgorithms.RsaSsaPssSha256, HttpStatusCode.OK)]
    public async Task Authenticate_ConfiguredAlgorithms_ReplaceDefaults(string algorithm, HttpStatusCode expected)
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
            options.Settings["SharedKernel:Security:Oidc:ValidAlgorithms:0"] = "PS256");
        string token = TestTokens.Create().SignedWith(TestTokens.RsaKey, algorithm).Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("JWT", HttpStatusCode.Unauthorized)]
    [InlineData("at+jwt", HttpStatusCode.OK)]
    public async Task Authenticate_ValidTokenTypesConfigured_RequiresMatchingTyp(string? type, HttpStatusCode expected)
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
            options.Settings["SharedKernel:Security:Oidc:ValidTokenTypes:0"] = "at+jwt");
        TokenBuilder builder = TestTokens.Create();
        if (type is not null)
        {
            builder.WithType(type);
        }

        using HttpResponseMessage response = await host.SendAsync(builder.Build());

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_TokenSignedByUnknownKey_Returns401()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        using var otherRsa = System.Security.Cryptography.RSA.Create(2048);
        string token = TestTokens.Create().SignedWith(new RsaSecurityKey(otherRsa) { KeyId = "rsa-1" }, SecurityAlgorithms.RsaSha256).Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_MalformedConfirmationClaim_Returns401()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create().WithClaim("cnf", new Dictionary<string, object> { ["jkt"] = 42 }).Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "MalformedConfirmation");
    }

    [Fact]
    public async Task Authenticate_TokenWithoutSubjectOrClientId_Returns401()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create().WithoutClaim("sub").Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLogged(new(12100));
    }
}
