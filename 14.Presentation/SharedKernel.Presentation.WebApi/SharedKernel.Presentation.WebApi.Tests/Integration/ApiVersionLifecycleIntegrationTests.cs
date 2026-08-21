using System.Globalization;
using Asp.Versioning;
using Asp.Versioning.Builder;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Presentation.WebApi.Versioning;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Integration tests for the RFC 8594 <c>Sunset</c>/<c>Deprecation</c>/<c>Link</c> response-header
/// capability (WO-063, P-413) using a real in-process Minimal API host.
/// </summary>
public sealed class ApiVersionLifecycleIntegrationTests : IClassFixture<ApiVersionLifecycleIntegrationTests.LifecycleWebAppFactory>
{
    private static readonly DateTimeOffset SunsetDateV2 = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Uri SuccessorUriV2 = new("https://api.example.com/v3");
    private static readonly Uri SuccessorUriV3 = new("https://api.example.com/v4");

    private readonly HttpClient _client;

    public ApiVersionLifecycleIntegrationTests(LifecycleWebAppFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task VersionWithDeclaredSunsetDate_CarriesSunsetHeader_RoundTripsAsRfc1123HttpDate()
    {
        var response = await _client.GetAsync("/v2.0/echo");

        response.Headers.Should().ContainKey("Sunset");
        var rawValue = response.Headers.GetValues("Sunset").Single();

        // Must round-trip through DateTimeOffset.Parse and match the RFC 1123 HTTP-date pattern
        // exactly — never an arbitrary string.
        var parsed = DateTimeOffset.Parse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None);
        parsed.Should().Be(SunsetDateV2);
        rawValue.Should().Be(SunsetDateV2.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task VersionWithSunsetDate_CarriesDeprecationHeader()
    {
        var response = await _client.GetAsync("/v2.0/echo");

        response.Headers.Should().ContainKey("Deprecation");
        response.Headers.GetValues("Deprecation").Single().Should().Be("true");
    }

    [Fact]
    public async Task VersionWithSunsetDateAndDeclaredSuccessor_CarriesSuccessorLinkHeader()
    {
        var response = await _client.GetAsync("/v2.0/echo");

        response.Headers.Should().ContainKey("Link");
        response.Headers.GetValues("Link").Single().Should().Be($"<{SuccessorUriV2}>; rel=\"successor-version\"");
    }

    [Fact]
    public async Task DeprecatedVersionWithNoSunsetDate_CarriesDeprecationHeader_RegardlessOfSunsetDate()
    {
        var response = await _client.GetAsync("/v3.0/echo");

        response.Headers.Should().ContainKey("Deprecation");
        response.Headers.GetValues("Deprecation").Single().Should().Be("true");
    }

    [Fact]
    public async Task DeprecatedVersionWithNoSunsetDate_NeverCarriesSunsetHeader()
    {
        var response = await _client.GetAsync("/v3.0/echo");

        response.Headers.Should().NotContainKey("Sunset");
    }

    [Fact]
    public async Task SuccessorDeclaredWithNoSunsetDate_NeverCarriesLinkHeader()
    {
        // v3.0 declares a successor URI but no sunset date — Link must only appear alongside a
        // configured sunset date (D-53).
        var response = await _client.GetAsync("/v3.0/echo");

        response.Headers.Should().NotContainKey("Link");
    }

    [Fact]
    public async Task VersionWithNoDeclaredLifecycleEntry_ProducesZeroNewHeaders()
    {
        var response = await _client.GetAsync("/v1.0/echo");

        response.Headers.Should().NotContainKey("Sunset");
        response.Headers.Should().NotContainKey("Deprecation");
        response.Headers.Should().NotContainKey("Link");
    }

    [Fact]
    public async Task ExistingReportApiVersionsHeaders_RemainPresentAlongsideNewLifecycleHeaders()
    {
        var response = await _client.GetAsync("/v2.0/echo");

        response.Headers.Should().ContainKey("api-supported-versions");
        response.Headers.Should().ContainKey("Sunset");
        response.Headers.Should().ContainKey("Deprecation");
    }

    public sealed class LifecycleWebAppFactory : WebApplicationFactory<LifecycleWebAppFactory>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            var appBuilder = WebApplication.CreateBuilder();
            appBuilder.WebHost.UseTestServer();

            appBuilder.Services.AddRouting();
            appBuilder.Services.AddSharedKernelApiVersioning(lifecycle =>
            {
                lifecycle.Configure(new ApiVersion(2, 0), SunsetDateV2, SuccessorUriV2);
                lifecycle.Configure(new ApiVersion(3, 0), sunsetDate: null, successor: SuccessorUriV3);
            });

            var app = appBuilder.Build();

            var versionSet = app.NewApiVersionSet()
                .HasApiVersion(new ApiVersion(1, 0))
                .HasDeprecatedApiVersion(new ApiVersion(2, 0))
                .HasDeprecatedApiVersion(new ApiVersion(3, 0))
                .ReportApiVersions()
                .Build();

            app.MapGet("/v{version:apiVersion}/echo", () => "ok")
                .WithApiVersionSet(versionSet)
                .HasApiVersion(new ApiVersion(1, 0))
                .HasApiVersion(new ApiVersion(2, 0))
                .HasApiVersion(new ApiVersion(3, 0));

            app.Start();
            return app;
        }
    }
}
