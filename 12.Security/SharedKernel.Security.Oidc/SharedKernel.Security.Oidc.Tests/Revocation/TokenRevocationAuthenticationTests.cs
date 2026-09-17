using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using SharedKernel.Security.Oidc.Revocation;
using SharedKernel.Security.Oidc.Tests.Infrastructure;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Revocation;

public sealed class TokenRevocationAuthenticationTests
{
    private readonly RecordingRevocationCheck _check = new();
    private readonly RecordingRevocationCache _cache = new();

    [Fact]
    public async Task Authenticate_TokenNotRevoked_Succeeds()
    {
        await using OidcTestHost host = await StartAsync();

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().Build());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_check.Requests);
    }

    [Fact]
    public async Task Authenticate_TokenRevoked_Returns401AndLogs()
    {
        _check.IsRevoked = _ => true;
        await using OidcTestHost host = await StartAsync();

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12103), "CheckAvailable", true);
    }

    [Fact]
    public async Task Authenticate_CheckThrows_Returns401AndLogsUnavailable()
    {
        _check.Failure = new HttpRequestException("introspection endpoint down");
        await using OidcTestHost host = await StartAsync();

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12103), "CheckAvailable", false);
    }

    [Fact]
    public async Task Authenticate_ValidToken_PassesTokenAndIdentifiersToCheck()
    {
        await using OidcTestHost host = await StartAsync();
        DateTime expires = DateTime.UtcNow.AddMinutes(30);
        string token = TestTokens.Create()
            .WithClaim("sub", "user-9")
            .WithClaim("jti", "token-id-1")
            .WithClaim("azp", "mobile-app")
            .WithClaim("sid", "session-3")
            .WithLifetime(DateTime.UtcNow.AddMinutes(-1), expires)
            .Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        TokenRevocationRequest request = Assert.Single(_check.Requests);
        Assert.Equal(token, request.Token);
        Assert.Equal(TestTokens.Sha256Base64Url(token), request.TokenHash);
        Assert.Equal("token-id-1", request.TokenId);
        Assert.Equal("user-9", request.SubjectId);
        Assert.Equal("mobile-app", request.ClientId);
        Assert.Equal("session-3", request.SessionId);
        Assert.Equal(new JsonWebToken(token).ValidTo, request.ExpiresAt.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, request.ExpiresAt.Offset);
    }

    [Fact]
    public async Task Authenticate_ServicePrincipalTokenWithoutSubject_PassesClientIdAsSubject()
    {
        await using OidcTestHost host = await StartAsync();
        string token = TestTokens.Create().WithoutClaim("sub").WithClaim("client_id", "daemon").Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        TokenRevocationRequest request = Assert.Single(_check.Requests);
        Assert.Equal("daemon", request.SubjectId);
        Assert.Equal("daemon", request.ClientId);
        Assert.Null(request.TokenId);
    }

    [Fact]
    public async Task Authenticate_InvalidToken_DoesNotCallCheck()
    {
        await using OidcTestHost host = await StartAsync();

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().WithAudience("api://other").Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_check.Requests);
    }

    [Fact]
    public async Task Authenticate_WithoutAddTokenRevocation_DoesNotCallRegisteredCheck()
    {
        _check.IsRevoked = _ => true;
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
            options.AfterOidc = services => services.AddSingleton<ITokenRevocationCheck>(_check));

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().Build());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_check.Requests);
    }

    [Fact]
    public async Task Authenticate_CachedRevokedAnswer_Returns401WithoutCallingCheck()
    {
        string token = TestTokens.Create().Build();
        _cache.Seed(TestTokens.Sha256Base64Url(token), isRevoked: true);
        await using OidcTestHost host = await StartAsync(withCache: true);

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_check.Requests);
    }

    [Fact]
    public async Task Authenticate_RepeatedRequests_UsesCachedAnswer()
    {
        await using OidcTestHost host = await StartAsync(withCache: true);
        string token = TestTokens.Create().Build();

        using HttpResponseMessage first = await host.SendAsync(token);
        using HttpResponseMessage second = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(_check.Requests);
        Assert.Equal(host.Clock.UtcNow.AddSeconds(30), Assert.Single(_cache.Writes).ExpiresAt);
    }

    [Fact]
    public async Task Authenticate_CacheWithoutAddTokenRevocation_HasNoEffect()
    {
        string token = TestTokens.Create().Build();
        _cache.Seed(TestTokens.Sha256Base64Url(token), isRevoked: true);
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
        {
            options.BeforeOidc = services => services.AddSingleton<ITokenRevocationCache>(_cache);
            options.Oidc = oidc => oidc.AddTokenRevocationCache<RecordingRevocationCache>();
        });

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<OidcTestHost> StartAsync(bool withCache = false) =>
        OidcTestHost.StartAsync(options =>
        {
            // Singleton instances registered first win over the scoped TryAdd registrations of the builder.
            options.BeforeOidc = services => services.AddSingleton<ITokenRevocationCheck>(_check);
            options.Oidc = oidc => oidc.AddTokenRevocation<RecordingRevocationCheck>();
            if (withCache)
            {
                options.BeforeOidc += services => services.AddSingleton<ITokenRevocationCache>(_cache);
                options.Oidc += oidc => oidc.AddTokenRevocationCache<RecordingRevocationCache>();
            }
        });
}
