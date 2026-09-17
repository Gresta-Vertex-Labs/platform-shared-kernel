using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Oidc.Dpop;
using SharedKernel.Security.Oidc.Tests.Infrastructure;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Authentication;

public sealed class CertificateBoundTokenTests : IDisposable
{
    private readonly X509Certificate2 _clientCertificate = new MtlsTestCertificateBuilder().WithSubjectName("CN=client-a").Build().Certificate;
    private readonly X509Certificate2 _otherCertificate = new MtlsTestCertificateBuilder().WithSubjectName("CN=client-b").Build().Certificate;
    private X509Certificate2? _presented;

    public void Dispose()
    {
        _clientCertificate.Dispose();
        _otherCertificate.Dispose();
    }

    [Fact]
    public async Task Authenticate_MatchingClientCertificate_SucceedsAndIsSenderConstrained()
    {
        await using OidcTestHost host = await StartAsync();
        _presented = _clientCertificate;

        UserResponse user = await host.GetUserAsync(TestTokens.Create().BoundToCertificate(_clientCertificate).Build());

        Assert.True(user.IsSenderConstrained);
    }

    [Fact]
    public async Task Authenticate_CertificateFromTlsFeature_Succeeds()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync(options => options.BeforeAuthentication = context =>
            context.Features.Set<ITlsConnectionFeature>(new TlsConnectionFeature { ClientCertificate = _clientCertificate }));

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().BoundToCertificate(_clientCertificate).Build());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_NoClientCertificate_Returns401()
    {
        await using OidcTestHost host = await StartAsync();

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().BoundToCertificate(_clientCertificate).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12105), "Reason", "CertificateMissing");
    }

    [Fact]
    public async Task Authenticate_DifferentClientCertificate_Returns401()
    {
        await using OidcTestHost host = await StartAsync();
        _presented = _otherCertificate;

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().BoundToCertificate(_clientCertificate).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        host.LogRecords.ShouldHaveLoggedWithProperty(new(12105), "Reason", "CertificateMismatch");
    }

    [Fact]
    public async Task Authenticate_SameSubjectDifferentKey_Returns401()
    {
        await using OidcTestHost host = await StartAsync();
        using X509Certificate2 lookalike = new MtlsTestCertificateBuilder().WithSubjectName("CN=client-a").Build().Certificate;
        _presented = lookalike;

        using HttpResponseMessage response = await host.SendAsync(TestTokens.Create().BoundToCertificate(_clientCertificate).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticate_ThumbprintInHexOrSha1_Returns401()
    {
        await using OidcTestHost host = await StartAsync();
        _presented = _clientCertificate;
        string sha1 = System.Buffers.Text.Base64Url.EncodeToString(SHA1.HashData(_clientCertificate.RawData));
        string hex = Convert.ToHexString(SHA256.HashData(_clientCertificate.RawData));

        using HttpResponseMessage sha1Response = await host.SendAsync(TestTokens.Create().WithClaim("cnf", new Dictionary<string, object> { ["x5t#S256"] = sha1 }).Build());
        using HttpResponseMessage hexResponse = await host.SendAsync(TestTokens.Create().WithClaim("cnf", new Dictionary<string, object> { ["x5t#S256"] = hex }).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, sha1Response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, hexResponse.StatusCode);
    }

    [Fact]
    public async Task Authenticate_UnboundTokenWithClientCertificate_Succeeds()
    {
        await using OidcTestHost host = await StartAsync();
        _presented = _otherCertificate;

        UserResponse user = await host.GetUserAsync(TestTokens.Create().Build());

        Assert.False(user.IsSenderConstrained);
    }

    [Fact]
    public async Task Authenticate_TokenBoundToDpopKeyAndCertificate_RequiresBoth()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var replayCache = new InMemoryDpopReplayCache();
        await using OidcTestHost host = await StartAsync(options =>
        {
            options.BeforeOidc = services => services.AddSingleton<IDpopReplayCache>(replayCache);
            options.Oidc = oidc => oidc.AddDpop<InMemoryDpopReplayCache>();
        });
        string token = TestTokens.Create()
            .WithClaim("cnf", new Dictionary<string, object>
            {
                ["jkt"] = HandmadeProof.Thumbprint(key),
                ["x5t#S256"] = TestTokens.CertificateThumbprint(_clientCertificate),
            })
            .Build();

        using HttpResponseMessage withoutCertificate = await host.SendAsync(
            token, "DPoP", HttpMethod.Get, dpopProofs: new HandmadeProof(key, token).WithMethod("GET").WithIssuedAt(host.Clock.UtcNow).Build());
        _presented = _clientCertificate;
        using HttpResponseMessage withCertificate = await host.SendAsync(
            token, "DPoP", HttpMethod.Get, dpopProofs: new HandmadeProof(key, token).WithMethod("GET").WithIssuedAt(host.Clock.UtcNow).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, withoutCertificate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withCertificate.StatusCode);
    }

    private Task<OidcTestHost> StartAsync(Action<OidcTestHostOptions>? configure = null) =>
        OidcTestHost.StartAsync(options =>
        {
            options.BeforeAuthentication = context => context.Connection.ClientCertificate = _presented;
            configure?.Invoke(options);
        });
}
