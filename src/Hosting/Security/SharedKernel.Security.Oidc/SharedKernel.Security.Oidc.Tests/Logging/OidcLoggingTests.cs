using System.Buffers.Text;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.Security.Oidc.Revocation;
using SharedKernel.Security.Oidc.Tests.Infrastructure;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Logging;

public sealed class OidcLoggingTests
{
    private const string DistinctiveSubject = "subject-hn63-distinctive";
    private const string DistinctiveClient = "client-hn63-distinctive";
    private const string DistinctiveJti = "jti-hn63-distinctive";
    private const string DistinctiveTenantValue = "tenant-hn63-not-a-guid";
    private const string DistinctiveRole = "role-hn63-distinctive";

    private static readonly EventId AlgorithmRejected = new(12104);

    private readonly RecordingRevocationCheck _check = new();

    [Fact]
    public async Task Authenticate_RevocationCheckThrows_Returns401AndLogsOneErrorWithExceptionAndNoIdentity()
    {
        var failure = new HttpRequestException("introspection endpoint down");
        _check.Failure = failure;
        await using OidcTestHost host = await StartWithRevocationAsync();
        string token = DistinctiveToken();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        LogRecord record = host.LogRecords.ShouldHaveLoggedWithException(new(12107), typeof(HttpRequestException));
        Assert.Same(failure, record.Exception);
        Assert.Equal(LogLevel.Error, record.LogLevel);
        host.LogRecords.ShouldHaveLoggedCount(new(12107), 1);
        host.LogRecords.ShouldNotHaveLogged(new(12103));
        host.LogRecords.ShouldNotLeak(token, token[^40..], DistinctiveSubject, DistinctiveClient, DistinctiveJti, DistinctiveRole);
    }

    [Fact]
    public async Task Authenticate_TokenRevoked_LogsRevokedWithoutExceptionAndNoFailureEvent()
    {
        _check.IsRevoked = _ => true;
        await using OidcTestHost host = await StartWithRevocationAsync();

        using HttpResponseMessage response = await host.SendAsync(DistinctiveToken());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithoutException(new(12103));
        host.LogRecords.ShouldNotHaveLogged(new(12107));
    }

    [Fact]
    public async Task Authenticate_AlgorithmHeaderVeryLong_LogsUnrecognized()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string alg = new('A', 5 * 1024);

        using HttpResponseMessage response = await host.SendAsync(RawToken(alg));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(AlgorithmRejected, "Algorithm", "unrecognized");
        host.LogRecords.ShouldNotLeak(alg);
    }

    [Theory]
    [InlineData("RS256\u0001\u0002")]
    [InlineData("RS\n384")]
    [InlineData("alg with space")]
    [InlineData("\"quoted\"")]
    [InlineData("AAAAAAAAAAAAAAAAA")]
    public async Task Authenticate_AlgorithmHeaderNotIdentifierShaped_LogsUnrecognized(string alg)
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();

        using HttpResponseMessage response = await host.SendAsync(RawToken(alg));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(AlgorithmRejected, "Algorithm", "unrecognized");
    }

    [Fact]
    public async Task Authenticate_SymmetricAlgorithm_StillLogsItsValue()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = TestTokens.Create().SignedWith(TestTokens.SymmetricKey, SecurityAlgorithms.HmacSha256).Build();

        using HttpResponseMessage response = await host.SendAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(AlgorithmRejected, "Algorithm", "HS256");
    }

    [Fact]
    public async Task Authenticate_UnsignedToken_StillLogsNone()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Unsigned(TestTokens.StandardPayload()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(AlgorithmRejected, "Algorithm", "none");
    }

    [Fact]
    public async Task Sweep_AllPaths_NeverLogTokenSubjectClientClaimValuesOrJti()
    {
        await using OidcTestHost host = await StartWithRevocationAsync();
        string valid = DistinctiveToken();
        string revoked = DistinctiveToken("jti-hn63-revoked");
        string badTenant = TestTokens.Create()
            .WithClaim("sub", DistinctiveSubject)
            .WithClaim("tenant_id", DistinctiveTenantValue)
            .Build();
        string noCaller = TestTokens.Create().WithoutClaim("sub").WithClaim("name", DistinctiveRole).Build();
        string wrongAudience = TestTokens.Create().WithClaim("sub", DistinctiveSubject).WithAudience("api://other-audience").Build();
        string expired = TestTokens.Create()
            .WithClaim("sub", DistinctiveSubject)
            .WithLifetime(DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddHours(-2))
            .Build();
        string wrongKey = TestTokens.Create()
            .WithClaim("sub", DistinctiveSubject)
            .SignedWith(TestTokens.RsaKey, SecurityAlgorithms.RsaSha384)
            .Build();
        _check.IsRevoked = request => request.TokenId == "jti-hn63-revoked";

        foreach (string token in new[] { valid, revoked, badTenant, noCaller, wrongAudience, expired, wrongKey })
        {
            using HttpResponseMessage response = await host.SendAsync(token);
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized);
        }

        _check.IsRevoked = _ => false;
        _check.Failure = new TimeoutException("check timed out");
        using HttpResponseMessage failed = await host.SendAsync(valid);

        Assert.NotEmpty(host.LogRecords);
        host.LogRecords.ShouldNotLeak(
            valid,
            revoked,
            badTenant,
            noCaller,
            wrongAudience,
            expired,
            wrongKey,
            DistinctiveSubject,
            DistinctiveClient,
            DistinctiveJti,
            "jti-hn63-revoked",
            DistinctiveTenantValue,
            DistinctiveRole);
    }

    private static string DistinctiveToken(string jti = DistinctiveJti) =>
        TestTokens.Create()
            .WithClaim("sub", DistinctiveSubject)
            .WithClaim("azp", DistinctiveClient)
            .WithClaim("jti", jti)
            .WithClaim("roles", new[] { DistinctiveRole })
            .Build();

    private static string RawToken(string alg)
    {
        string header = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["alg"] = alg, ["typ"] = "JWT" }));
        string body = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(TestTokens.StandardPayload()));
        return $"{header}.{body}.{Base64Url.EncodeToString("signature"u8)}";
    }

    private Task<OidcTestHost> StartWithRevocationAsync() =>
        OidcTestHost.StartAsync(options =>
        {
            options.BeforeOidc = services => services.AddSingleton<ITokenRevocationCheck>(_check);
            options.Oidc = oidc => oidc.AddTokenRevocation<RecordingRevocationCheck>();
        });
}
