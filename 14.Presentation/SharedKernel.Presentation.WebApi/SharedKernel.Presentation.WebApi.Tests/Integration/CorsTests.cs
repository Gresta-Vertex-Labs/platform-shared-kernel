using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Options;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Design D7/D16: CORS is denied by default, configured from settings, exposes the platform's response headers, and
/// refuses credentials without explicit origins at startup.
/// </summary>
public sealed class CorsTests
{
    private const string AllowedOrigin = "https://app.example.com";

    private const string OtherOrigin = "https://evil.example.com";

    [Fact]
    public async Task AllowedOrigin_GetsTheAllowOriginHeader_AndThePlatformExposedHeaders()
    {
        await using var app = await StartAsync(options => options.Cors.AllowedOrigins.Add(AllowedOrigin));

        using var response = await SendAsync(app, HttpMethod.Get, AllowedOrigin);

        response.Headers.GetValues(HeaderNames.AccessControlAllowOrigin).Should().ContainSingle().Which.Should().Be(AllowedOrigin);
        var exposed = string.Join(",", response.Headers.GetValues(HeaderNames.AccessControlExposeHeaders))
            .Split(',', StringSplitOptions.TrimEntries);
        exposed.Should().BeEquivalentTo(
            WellKnownHeaders.CorrelationId,
            HeaderNames.ETag,
            HeaderNames.Location,
            HeaderNames.RetryAfter,
            "Sunset",
            "Deprecation",
            HeaderNames.Link,
            "api-supported-versions",
            "api-deprecated-versions");
    }

    [Fact]
    public async Task OtherOrigin_GetsNoAllowOriginHeader()
    {
        await using var app = await StartAsync(options => options.Cors.AllowedOrigins.Add(AllowedOrigin));

        using var response = await SendAsync(app, HttpMethod.Get, OtherOrigin);

        response.Headers.Contains(HeaderNames.AccessControlAllowOrigin).Should().BeFalse();
    }

    [Fact]
    public async Task WithoutConfiguredOrigins_NoOriginIsAllowed()
    {
        await using var app = await StartAsync(configure: null);

        using var response = await SendAsync(app, HttpMethod.Get, AllowedOrigin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains(HeaderNames.AccessControlAllowOrigin).Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_IsAnswered_WithTheConfiguredMaxAge()
    {
        await using var app = await StartAsync(options =>
        {
            options.Cors.AllowedOrigins.Add(AllowedOrigin);
            options.Cors.AllowedMethods.Add("PUT");
            options.Cors.PreflightMaxAge = TimeSpan.FromMinutes(20);
        });
        using var request = new HttpRequestMessage(HttpMethod.Options, "/data");
        request.Headers.Add(HeaderNames.Origin, AllowedOrigin);
        request.Headers.Add(HeaderNames.AccessControlRequestMethod, "PUT");

        using var response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues(HeaderNames.AccessControlAllowMethods).Should().ContainSingle().Which.Should().Be("PUT");
        response.Headers.GetValues(HeaderNames.AccessControlMaxAge).Should().ContainSingle().Which.Should().Be("1200");
    }

    [Fact]
    public async Task OriginsAndExposedHeaders_BindFromConfiguration()
    {
        var section = WebApiOptions.SectionName + ":Cors:";
        await using var app = await WebApiTestHost.StartAsync(
            MapEndpoint,
            configuration: new Dictionary<string, string?>
            {
                [section + "AllowedOrigins:0"] = AllowedOrigin,
                [section + "ExposedHeaders:0"] = "X-Total-Count",
            });

        using var response = await SendAsync(app, HttpMethod.Get, AllowedOrigin);

        response.Headers.GetValues(HeaderNames.AccessControlAllowOrigin).Should().ContainSingle().Which.Should().Be(AllowedOrigin);
        string.Join(",", response.Headers.GetValues(HeaderNames.AccessControlExposeHeaders))
            .Should().Contain("X-Total-Count").And.Contain(WellKnownHeaders.CorrelationId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    public async Task CredentialsWithoutExplicitOrigins_FailAtStartup(string? origin)
    {
        var logs = new InMemoryLoggerFactory();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = WebApiTestHost.Production });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ILoggerFactory>(logs);
        builder.AddSharedKernelWebApi(options =>
        {
            options.Cors.AllowCredentials = true;
            if (origin is not null)
            {
                options.Cors.AllowedOrigins.Add(origin);
            }
        });
        await using var app = builder.Build();

        var act = () => app.StartAsync();

        (await act.Should().ThrowAsync<OptionsValidationException>()).Which.Message.Should().Contain("AllowCredentials");
        var logger = logs.GetLogger("SharedKernel.Presentation.WebApi.Options.WebApiOptionsValidator");
        logger.Records.Should().Contain(record =>
            record.EventId.Id == LoggingEventIdRanges.Presentation + 4 && record.LogLevel == LogLevel.Critical);
    }

    [Fact]
    public async Task CredentialsWithExplicitOrigins_AreAllowed()
    {
        await using var app = await StartAsync(options =>
        {
            options.Cors.AllowedOrigins.Add(AllowedOrigin);
            options.Cors.AllowCredentials = true;
        });

        using var response = await SendAsync(app, HttpMethod.Get, AllowedOrigin);

        response.Headers.GetValues(HeaderNames.AccessControlAllowCredentials).Should().ContainSingle().Which.Should().Be("true");
    }

    private static Task<WebApplication> StartAsync(Action<WebApiOptions>? configure) =>
        WebApiTestHost.StartAsync(MapEndpoint, configureOptions: configure);

    private static void MapEndpoint(WebApplication app)
    {
        app.MapGet("/data", () => "ok");
        app.MapPut("/data", () => "ok");
    }

    private static Task<HttpResponseMessage> SendAsync(WebApplication app, HttpMethod method, string origin)
    {
        var request = new HttpRequestMessage(method, "/data");
        request.Headers.Add(HeaderNames.Origin, origin);
        return app.GetTestClient().SendAsync(request);
    }
}
