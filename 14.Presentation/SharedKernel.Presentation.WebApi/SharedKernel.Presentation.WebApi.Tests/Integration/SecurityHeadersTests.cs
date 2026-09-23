using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Options;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Design D6/D16: the security headers on every response (error responses included), the per-endpoint CSP opt-out,
/// headers an endpoint set itself kept, and HSTS only over HTTPS outside Development.
/// </summary>
public sealed class SecurityHeadersTests
{
    private const string ReferrerPolicy = "Referrer-Policy";

    private const string PermissionsPolicy = "Permissions-Policy";

    private const string ApiHost = "https://api.example.com/";

    [Theory]
    [InlineData("/data")]
    [InlineData("/missing")]
    public async Task EveryResponse_CarriesTheDefaultSecurityHeaders(string path)
    {
        await using var app = await StartAsync();

        using var response = await app.GetTestClient().GetAsync(path);

        Single(response, HeaderNames.XContentTypeOptions).Should().Be("nosniff");
        Single(response, HeaderNames.XFrameOptions).Should().Be("DENY");
        Single(response, ReferrerPolicy).Should().Be("no-referrer");
        Single(response, PermissionsPolicy).Should().Be("geolocation=(), microphone=(), camera=()");
        Single(response, HeaderNames.ContentSecurityPolicy).Should().Be("default-src 'none'; frame-ancestors 'none'");
    }

    [Fact]
    public async Task WithContentSecurityPolicyNull_SendsNoPolicy_ForThatEndpointOnly()
    {
        await using var app = await StartAsync();

        using var docs = await app.GetTestClient().GetAsync("/docs");
        using var data = await app.GetTestClient().GetAsync("/data");

        docs.Headers.Contains(HeaderNames.ContentSecurityPolicy).Should().BeFalse();
        Single(docs, HeaderNames.XContentTypeOptions).Should().Be("nosniff");
        data.Headers.Contains(HeaderNames.ContentSecurityPolicy).Should().BeTrue();
    }

    [Fact]
    public async Task WithContentSecurityPolicy_ReplacesThePolicy_ForThatEndpoint()
    {
        await using var app = await StartAsync();

        using var response = await app.GetTestClient().GetAsync("/page");

        Single(response, HeaderNames.ContentSecurityPolicy).Should().Be("default-src 'self'");
    }

    [Fact]
    public async Task HeaderTheEndpointSet_IsKept()
    {
        await using var app = await StartAsync();

        using var response = await app.GetTestClient().GetAsync("/framed");

        Single(response, HeaderNames.XFrameOptions).Should().Be("SAMEORIGIN");
    }

    [Fact]
    public async Task EmptyValue_TurnsAHeaderOff()
    {
        await using var app = await StartAsync(options =>
        {
            options.SecurityHeaders.ReferrerPolicy = null;
            options.SecurityHeaders.ContentSecurityPolicy = string.Empty;
        });

        using var response = await app.GetTestClient().GetAsync("/data");

        response.Headers.Contains(ReferrerPolicy).Should().BeFalse();
        response.Headers.Contains(HeaderNames.ContentSecurityPolicy).Should().BeFalse();
        response.Headers.Contains(HeaderNames.XFrameOptions).Should().BeTrue();
    }

    [Fact]
    public async Task Disabled_SendsNoSecurityHeaders_AndNoHsts()
    {
        await using var app = await StartAsync(options => options.SecurityHeaders.Enabled = false);
        using var client = HttpsClient(app);

        using var response = await client.GetAsync("data");

        response.Headers.Contains(HeaderNames.XContentTypeOptions).Should().BeFalse();
        response.Headers.Contains(HeaderNames.StrictTransportSecurity).Should().BeFalse();
    }

    [Fact]
    public async Task Hsts_IsSentOverHttps_OutsideDevelopment()
    {
        await using var app = await StartAsync();
        using var client = HttpsClient(app);

        using var response = await client.GetAsync("data");

        Single(response, HeaderNames.StrictTransportSecurity).Should().Be("max-age=31536000; includeSubDomains");
    }

    [Fact]
    public async Task Hsts_FollowsTheSettings()
    {
        await using var app = await StartAsync(options =>
        {
            options.SecurityHeaders.HstsMaxAge = TimeSpan.FromDays(1);
            options.SecurityHeaders.HstsIncludeSubDomains = false;
            options.SecurityHeaders.HstsPreload = true;
        });
        using var client = HttpsClient(app);

        using var response = await client.GetAsync("data");

        Single(response, HeaderNames.StrictTransportSecurity).Should().Be("max-age=86400; preload");
    }

    [Fact]
    public async Task Hsts_IsNeverSentOverHttp()
    {
        await using var app = await StartAsync();

        using var response = await app.GetTestClient().GetAsync("/data");

        response.Headers.Contains(HeaderNames.StrictTransportSecurity).Should().BeFalse();
    }

    [Fact]
    public async Task Hsts_IsNeverSentInDevelopment()
    {
        await using var app = await StartAsync(environment: WebApiTestHost.Development);
        using var client = HttpsClient(app);

        using var response = await client.GetAsync("data");

        response.Headers.Contains(HeaderNames.StrictTransportSecurity).Should().BeFalse();
    }

    private static Task<WebApplication> StartAsync(Action<WebApiOptions>? configure = null, string environment = WebApiTestHost.Production) =>
        WebApiTestHost.StartAsync(
            app =>
            {
                app.MapGet("/data", () => "ok");
                app.MapGet("/docs", () => "<html></html>").WithContentSecurityPolicy(null);
                app.MapGet("/page", () => "<html></html>").WithContentSecurityPolicy("default-src 'self'");
                app.MapGet("/framed", (HttpContext context) =>
                {
                    context.Response.Headers[HeaderNames.XFrameOptions] = "SAMEORIGIN";
                    return "ok";
                });
            },
            configureOptions: configure,
            environment: environment);

    private static HttpClient HttpsClient(WebApplication app)
    {
        var client = app.GetTestServer().CreateClient();
        client.BaseAddress = new Uri(ApiHost);
        return client;
    }

    private static string Single(HttpResponseMessage response, string header) =>
        response.Headers.GetValues(header).Should().ContainSingle().Subject;
}
