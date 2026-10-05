using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Oidc.Dpop;
using SharedKernel.Security.Oidc.Tests.Infrastructure;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Dpop;

public sealed class DpopAuthenticationTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly InMemoryDpopReplayCache _replayCache = new();

    public void Dispose() => _key.Dispose();

    [Fact]
    public async Task Authenticate_BoundTokenWithStandardProof_Succeeds()
    {
        await using OidcTestHost host = await StartAsync();
        string token = TestTokens.Create().BoundToDpopKey(HandmadeProof.Thumbprint(_key)).Build();
        DpopTestProof proof = new DpopTestProofBuilder()
            .WithKey(_key)
            .WithHttpMethod("GET")
            .WithHttpUri("https://api.example.test/resource")
            .WithIssuedAt(host.Clock.UtcNow)
            .WithAccessToken(token)
            .Build();

        UserResponse user = await host.GetUserAsync(token, "DPoP", HttpMethod.Get, proof.ProofJwt);

        Assert.True(user.IsSenderConstrained);
    }

    [Fact]
    public async Task Authenticate_BoundTokenWithValidProof_SucceedsAndIsSenderConstrained()
    {
        await using OidcTestHost host = await StartAsync();
        string token = BoundToken();

        UserResponse user = await host.GetUserAsync(token, "DPoP", HttpMethod.Get, Proof(host, token).Build());

        Assert.Equal("user-1", user.SubjectId);
        Assert.True(user.IsSenderConstrained);
        Assert.Equal(1, _replayCache.Count);
    }

    [Fact]
    public async Task Authenticate_LowerCaseDpopScheme_Succeeds()
    {
        await using OidcTestHost host = await StartAsync();
        string token = BoundToken();

        using HttpResponseMessage response = await host.SendAsync(token, "dpop", HttpMethod.Get, dpopProofs: Proof(host, token).Build());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_ReplayedProof_Returns401()
    {
        await using OidcTestHost host = await StartAsync();
        string token = BoundToken();
        string proof = Proof(host, token).Build();

        using HttpResponseMessage first = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: proof);
        using HttpResponseMessage second = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: proof);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "Replayed");
    }

    [Fact]
    public async Task Authenticate_ProofForOtherMethod_Returns401WithInvalidProofChallenge()
    {
        await using OidcTestHost host = await StartAsync();
        string token = BoundToken();

        using HttpResponseMessage response = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: Proof(host, token, method: "POST").Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("DPoP algs=\"ES256 PS256 RS256\", error=\"invalid_dpop_proof\"", WwwAuthenticate(response), StringComparison.Ordinal);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "MethodMismatch");
    }

    [Fact]
    public async Task Authenticate_ProofForOtherUri_Returns401()
    {
        await using OidcTestHost host = await StartAsync();
        string token = BoundToken();
        string proof = Proof(host, token).WithUri("https://api.example.test/roles/admin").Build();

        using HttpResponseMessage response = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: proof);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "UriMismatch");
    }

    [Theory]
    [InlineData(-120)]
    [InlineData(30)]
    public async Task Authenticate_ProofOutsideLifetime_Returns401(int offsetSeconds)
    {
        await using OidcTestHost host = await StartAsync();
        string token = BoundToken();
        string proof = Proof(host, token).WithIssuedAt(host.Clock.UtcNow.AddSeconds(offsetSeconds)).Build();

        using HttpResponseMessage response = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: proof);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "Expired");
    }

    [Fact]
    public async Task Authenticate_MissingProofHeader_Returns401()
    {
        await using OidcTestHost host = await StartAsync();

        using HttpResponseMessage response = await host.SendAsync(BoundToken(), "DPoP", HttpMethod.Get);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "MissingProof");
    }

    [Fact]
    public async Task Authenticate_TwoProofHeaders_Returns401()
    {
        await using OidcTestHost host = await StartAsync();
        string token = BoundToken();

        using HttpResponseMessage response = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: [Proof(host, token).Build(), Proof(host, token).Build()]);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "MultipleProofs");
    }

    [Fact]
    public async Task Authenticate_ProofWithWrongType_Returns401()
    {
        await using OidcTestHost host = await StartAsync();
        string token = BoundToken();

        using HttpResponseMessage response = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: Proof(host, token).WithHeader("typ", "JWT").Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "InvalidType");
    }

    [Fact]
    public async Task Authenticate_ProofSignedByOtherKeyThanConfirmation_Returns401()
    {
        await using OidcTestHost host = await StartAsync();
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string token = BoundToken();
        string proof = new HandmadeProof(attackerKey, token).WithMethod("GET").WithIssuedAt(host.Clock.UtcNow).Build();

        using HttpResponseMessage response = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: proof);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "KeyMismatch");
    }

    [Fact]
    public async Task Authenticate_ProofForOtherAccessToken_Returns401()
    {
        await using OidcTestHost host = await StartAsync();
        string token = BoundToken();
        string otherToken = TestTokens.Create().WithClaim("sub", "user-2").BoundToDpopKey(HandmadeProof.Thumbprint(_key)).Build();

        using HttpResponseMessage response = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: Proof(host, otherToken).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "AccessTokenHashMismatch");
    }

    [Fact]
    public async Task Authenticate_ProofJwkWithPrivateKey_Returns401()
    {
        await using OidcTestHost host = await StartAsync();
        string token = BoundToken();
        ECParameters parameters = _key.ExportParameters(includePrivateParameters: true);
        string proof = Proof(host, token).WithJwkMember("d", System.Buffers.Text.Base64Url.EncodeToString(parameters.D)).Build();

        using HttpResponseMessage response = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: proof);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "PrivateKeyInProof");
    }

    [Fact]
    public async Task Authenticate_BoundTokenPresentedAsBearer_Returns401()
    {
        await using OidcTestHost host = await StartAsync();
        string token = BoundToken();

        using HttpResponseMessage response = await host.SendAsync(token, "Bearer", HttpMethod.Get, dpopProofs: Proof(host, token).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "BoundTokenPresentedAsBearer");
        Assert.Equal(0, _replayCache.Count);
    }

    [Fact]
    public async Task Authenticate_UnboundTokenPresentedWithDpopScheme_Returns401()
    {
        await using OidcTestHost host = await StartAsync();
        string token = TestTokens.Create().Build();

        using HttpResponseMessage response = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: Proof(host, token).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "UnboundTokenPresentedAsDpop");
    }

    [Fact]
    public async Task Authenticate_UnboundBearerTokenInAllowedMode_Succeeds()
    {
        await using OidcTestHost host = await StartAsync();

        UserResponse user = await host.GetUserAsync(TestTokens.Create().Build());

        Assert.False(user.IsSenderConstrained);
    }

    [Fact]
    public async Task Authenticate_BoundTokenAsBearerWithoutAddDpop_Returns401()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();

        using HttpResponseMessage response = await host.SendAsync(BoundToken());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "DpopNotEnabled");
    }

    [Fact]
    public async Task Authenticate_BoundTokenWithDpopSchemeWithoutAddDpop_Returns401WithoutDpopChallenge()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        string token = BoundToken();

        using HttpResponseMessage response = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: Proof(host, token).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("DPoP", WwwAuthenticate(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticate_RequiredModeWithUnboundBearerToken_Returns401()
    {
        await using OidcTestHost host = await StartAsync(options => options.Settings["SharedKernel:Security:Oidc:Dpop:Mode"] = "Required");

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "DpopRequired");
    }

    [Fact]
    public async Task Authenticate_RequiredModeWithBoundToken_Succeeds()
    {
        await using OidcTestHost host = await StartAsync(options => options.Settings["SharedKernel:Security:Oidc:Dpop:Mode"] = "Required");
        string token = BoundToken();

        using HttpResponseMessage response = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: Proof(host, token).Build());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_NonceRequiredAndMissing_ChallengesWithNonceThenAcceptsIt()
    {
        await using OidcTestHost host = await StartAsync(options => options.Settings["SharedKernel:Security:Oidc:Dpop:RequireNonce"] = "true");
        string token = BoundToken();

        using HttpResponseMessage first = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: Proof(host, token).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        string challenge = WwwAuthenticate(first);
        Assert.Contains("DPoP algs=", challenge, StringComparison.Ordinal);
        Assert.Contains("error=\"use_dpop_nonce\"", challenge, StringComparison.Ordinal);
        string nonce = Assert.Single(first.Headers.GetValues("DPoP-Nonce"));
        Assert.Equal(0, _replayCache.Count);

        using HttpResponseMessage second = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: Proof(host, token).WithNonce(nonce).Build());

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Challenge_NonceRequiredAndOtherProofError_OmitsNonce()
    {
        await using OidcTestHost host = await StartAsync(options => options.Settings["SharedKernel:Security:Oidc:Dpop:RequireNonce"] = "true");
        string token = BoundToken();

        using HttpResponseMessage response = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: Proof(host, token, method: "DELETE").Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("DPoP-Nonce"));
    }

    [Fact]
    public async Task Challenge_NoTokenWithDpopEnabled_AdvertisesDpopAlgorithms()
    {
        await using OidcTestHost host = await StartAsync();

        using HttpResponseMessage response = await host.SendAsync(token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        string challenge = WwwAuthenticate(response);
        Assert.Contains("Bearer", challenge, StringComparison.Ordinal);
        Assert.Contains("DPoP algs=\"ES256 PS256 RS256\"", challenge, StringComparison.Ordinal);
        Assert.DoesNotContain("error=\"invalid_dpop_proof\"", challenge, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Challenge_ConfiguredDpopAlgorithms_AreAdvertisedAndEnforced()
    {
        await using OidcTestHost host = await StartAsync(options => options.Settings["SharedKernel:Security:Oidc:Dpop:ValidAlgorithms:0"] = "PS256");
        string token = BoundToken();

        using HttpResponseMessage response = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: Proof(host, token).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("DPoP algs=\"PS256\"", WwwAuthenticate(response), StringComparison.Ordinal);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12102), "Reason", "AlgorithmNotAllowed");
    }

    [Fact]
    public async Task Challenge_DpopNotEnabled_HasNoDpopChallenge()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();

        using HttpResponseMessage response = await host.SendAsync(token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("DPoP", WwwAuthenticate(response), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("DPoP-Nonce"));
    }

    [Fact]
    public async Task Authenticate_ReplayCacheThrows_FailsRequest()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
        {
            options.BeforeOidc = services => services.AddSingleton<IDpopReplayCache, ThrowingReplayCache>();
            options.Oidc = oidc => oidc.AddDpop<ThrowingReplayCache>();
        });
        string token = BoundToken();

        using HttpResponseMessage response = await host.SendAsync(token, "DPoP", HttpMethod.Get, dpopProofs: Proof(host, token).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string WwwAuthenticate(HttpResponseMessage response) =>
        response.Headers.NonValidated.TryGetValues("WWW-Authenticate", out HeaderStringValues values) ? string.Join(" | ", values) : string.Empty;

    private Task<OidcTestHost> StartAsync(Action<OidcTestHostOptions>? configure = null) =>
        OidcTestHost.StartAsync(options =>
        {
            options.BeforeOidc = services => services.AddSingleton<IDpopReplayCache>(_replayCache);
            options.Oidc = oidc => oidc.AddDpop<InMemoryDpopReplayCache>();
            configure?.Invoke(options);
        });

    private string BoundToken() => TestTokens.Create().BoundToDpopKey(HandmadeProof.Thumbprint(_key)).Build();

    private HandmadeProof Proof(OidcTestHost host, string token, string method = "GET") =>
        new HandmadeProof(_key, token).WithMethod(method).WithIssuedAt(host.Clock.UtcNow);

    private sealed class ThrowingReplayCache : IDpopReplayCache
    {
        public ValueTask<bool> TryAddAsync(string proofId, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Replay store unavailable.");
    }
}
