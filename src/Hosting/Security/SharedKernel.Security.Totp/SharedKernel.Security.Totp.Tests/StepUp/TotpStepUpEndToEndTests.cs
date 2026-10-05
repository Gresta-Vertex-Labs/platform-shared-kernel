using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Oidc;
using SharedKernel.Security.Oidc.Extensions;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Cryptography;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.StepUp;

// The real OIDC mapper and IUserContext registration, with the JWT handler swapped for one that trusts test headers.
public sealed class TotpStepUpEndToEndTests : IAsyncLifetime
{
    private const string SubjectHeader = "X-Test-Subject";
    private const string SessionHeader = "X-Test-Session";
    private const string OtherSubject = "22222222-2222-2222-2222-222222222222";

    private static readonly byte[] Secret = [.. Enumerable.Range(1, 20).Select(value => (byte)(value * 13))];

    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 3, 1, 12, 0, 10, TimeSpan.Zero));
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Security:Oidc:Authority"] = "https://idp.example.test",
                ["SharedKernel:Security:Oidc:Audiences:0"] = "api",
            })
            .Build();

        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IClock>(_clock);
                    services.AddSingleton<ITotpReplayGuard>(new FakeTotpReplayGuard(_clock));
                    services.AddSingleton<ITotpStepUpStore>(new InMemoryTotpStepUpStore());
                    services.AddSingleton<IRecoveryCodeStore>(new InMemoryRecoveryCodeStore());
                    services.AddOidcAuthentication(configuration);
                    services.Configure<AuthenticationOptions>(options =>
                        options.SchemeMap[OidcAuthenticationDefaults.AuthenticationScheme].HandlerType = typeof(HeaderAuthenticationHandler));
                    services.AddSharedKernelCryptography(configuration).AddTotpStepUp<InMemoryTotpStepUpStore, InMemoryRecoveryCodeStore>();
                })
                .Configure(app =>
                {
                    app.UseAuthentication();
                    app.Run(HandleAsync);
                }))
            .StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task WasAuthenticatedWithOtp_AfterVerifyCodeInSession_IsTrueOnLaterRequestsOfThatSessionOnly()
    {
        string code = new TotpGenerator(_clock).GenerateCode(Secret);

        string before = await GetAsync("/otp", FakeUserContext.DefaultSubjectId, "session-1");
        string verify = await GetAsync($"/verify?code={code}", FakeUserContext.DefaultSubjectId, "session-1");
        string after = await GetAsync("/otp", FakeUserContext.DefaultSubjectId, "session-1");
        string otherSession = await GetAsync("/otp", FakeUserContext.DefaultSubjectId, "session-2");
        string otherUserSameSession = await GetAsync("/otp", OtherSubject, "session-1");

        Assert.Equal("False", before);
        Assert.Equal(nameof(TotpChallengeResult.Verified), verify);
        Assert.Equal("True", after);
        Assert.Equal("False", otherSession);
        Assert.Equal("False", otherUserSameSession);
    }

    [Fact]
    public async Task WasAuthenticatedWithOtp_AfterFreshnessWindow_IsFalseAgain()
    {
        string code = new TotpGenerator(_clock).GenerateCode(Secret);
        await GetAsync($"/verify?code={code}", FakeUserContext.DefaultSubjectId, "session-1");

        _clock.Advance(TimeSpan.FromMinutes(15));
        string atWindowEnd = await GetAsync("/otp", FakeUserContext.DefaultSubjectId, "session-1");
        _clock.Advance(TimeSpan.FromSeconds(1));
        string afterWindow = await GetAsync("/otp", FakeUserContext.DefaultSubjectId, "session-1");

        Assert.Equal("True", atWindowEnd);
        Assert.Equal("False", afterWindow);
    }

    [Fact]
    public async Task VerifyCode_WrongCodeOrReplayInOtherSession_DoesNotStepUp()
    {
        string code = new TotpGenerator(_clock).GenerateCode(Secret);
        string wrong = code == "000000" ? "111111" : "000000";

        string wrongResult = await GetAsync($"/verify?code={wrong}", FakeUserContext.DefaultSubjectId, "session-1");
        string first = await GetAsync($"/verify?code={code}", FakeUserContext.DefaultSubjectId, "session-1");
        string replay = await GetAsync($"/verify?code={code}", FakeUserContext.DefaultSubjectId, "session-2");
        string otherSession = await GetAsync("/otp", FakeUserContext.DefaultSubjectId, "session-2");

        Assert.NotEqual(nameof(TotpChallengeResult.Verified), wrongResult);
        Assert.Equal(nameof(TotpChallengeResult.Verified), first);
        Assert.Equal(nameof(TotpChallengeResult.Replayed), replay);
        Assert.Equal("False", otherSession);
    }

    [Fact]
    public async Task VerifyCode_UnauthenticatedRequest_ReturnsNoSession()
    {
        string code = new TotpGenerator(_clock).GenerateCode(Secret);

        string result = await GetAsync($"/verify?code={code}", subjectId: null, sessionId: null);

        Assert.Equal(nameof(TotpChallengeResult.NoSession), result);
    }

    [Fact]
    public async Task AuthenticationMethodTime_AfterVerifyCode_IsTheVerificationTime_UntilTheWindowEnds()
    {
        // X1: the real OIDC mapper reads the time the transformation stamps; it stays the verification time as the
        // clock moves, which is what lets a maximum age end the step-up on a long-lived connection.
        DateTimeOffset verifiedAt = _clock.UtcNow;
        string code = new TotpGenerator(_clock).GenerateCode(Secret);

        string before = await GetAsync("/otp-time", FakeUserContext.DefaultSubjectId, "session-1");
        await GetAsync($"/verify?code={code}", FakeUserContext.DefaultSubjectId, "session-1");
        string justAfter = await GetAsync("/otp-time", FakeUserContext.DefaultSubjectId, "session-1");
        _clock.Advance(TimeSpan.FromMinutes(10));
        string tenMinutesLater = await GetAsync("/otp-time", FakeUserContext.DefaultSubjectId, "session-1");
        _clock.Advance(TimeSpan.FromMinutes(6));
        string afterWindow = await GetAsync("/otp-time", FakeUserContext.DefaultSubjectId, "session-1");

        string expected = verifiedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        Assert.Equal("none", before);
        Assert.Equal(expected, justAfter);
        Assert.Equal(expected, tenMinutesLater);
        Assert.Equal("none", afterWindow);
    }

    [Fact]
    public async Task UserContext_ResolvedByOidcMapper_KeepsPrimaryMethods()
    {
        string code = new TotpGenerator(_clock).GenerateCode(Secret);
        await GetAsync($"/verify?code={code}", FakeUserContext.DefaultSubjectId, "session-1");

        string methods = await GetAsync("/methods", FakeUserContext.DefaultSubjectId, "session-1");

        Assert.Equal("otp,pwd", methods);
    }

    private async Task<string> GetAsync(string path, string? subjectId, string? sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (subjectId is not null)
        {
            request.Headers.Add(SubjectHeader, subjectId);
        }

        if (sessionId is not null)
        {
            request.Headers.Add(SessionHeader, sessionId);
        }

        using HttpResponseMessage response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task HandleAsync(HttpContext context)
    {
        IUserContext user = context.RequestServices.GetRequiredService<IUserContext>();
        string body = context.Request.Path.Value switch
        {
            "/otp" => user.WasAuthenticatedWith("otp").ToString(),
            "/otp-time" => user.GetAuthenticationMethodTime("otp")?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) ?? "none",
            "/methods" => string.Join(',', user.AuthenticationMethods.Order(StringComparer.Ordinal)),
            "/verify" => (await context.RequestServices.GetRequiredService<TotpChallengeService>()
                .VerifyCodeAsync(user, Secret, context.Request.Query["code"].ToString(), cancellationToken: context.RequestAborted)).ToString(),
            _ => string.Empty,
        };
        await context.Response.WriteAsync(body);
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? subjectId = Request.Headers[SubjectHeader].FirstOrDefault();
            if (subjectId is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new SecurityTestContextBuilder()
                .WithSubjectId(subjectId)
                .WithSessionId(Request.Headers[SessionHeader].FirstOrDefault())
                .WithAuthenticationMethods("pwd")
                .Build();
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
