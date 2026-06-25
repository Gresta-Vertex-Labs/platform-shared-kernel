using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Presentation.WebApi.Versioning;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="ApiVersioningExtensions.AddSharedKernelApiVersioning"/> using a
/// real in-process Minimal API host (<see cref="WebApplicationFactory{TEntryPoint}"/>).
/// </summary>
/// <remarks>
/// Verifies: unversioned requests fall back to <see cref="SharedKernelApiVersioningDefaults.DefaultApiVersion"/>,
/// the URL-segment reader works, the header reader works, and <c>ReportApiVersions = true</c> adds the
/// <c>api-supported-versions</c> response header.
/// </remarks>
public sealed class ApiVersioningIntegrationTests : IClassFixture<ApiVersioningIntegrationTests.VersioningWebAppFactory>
{
    private readonly HttpClient _client;

    public ApiVersioningIntegrationTests(VersioningWebAppFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task UnversionedRequest_FallsBackToDefaultApiVersion()
    {
        var response = await _client.GetAsync("/echo-version");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("1.0");
    }

    [Fact]
    public async Task UrlSegmentVersionReader_ResolvesRequestedVersion()
    {
        var response = await _client.GetAsync("/v1/echo-version");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("1"); // ApiVersion("1") parsed from the URL segment has no minor component.
    }

    [Fact]
    public async Task HeaderVersionReader_ResolvesRequestedVersion()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/echo-version");
        request.Headers.Add("X-Api-Version", "1.0");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("1.0");
    }

    [Fact]
    public async Task Response_IncludesApiSupportedVersionsHeader()
    {
        var response = await _client.GetAsync("/echo-version");

        response.Headers.Should().ContainKey("api-supported-versions");
    }

    public sealed class VersioningWebAppFactory : WebApplicationFactory<VersioningWebAppFactory>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            var appBuilder = WebApplication.CreateBuilder();
            appBuilder.WebHost.UseTestServer();

            appBuilder.Services.AddRouting();
            appBuilder.Services.AddSharedKernelApiVersioning();

            var app = appBuilder.Build();

            var apiVersionSet = app.NewApiVersionSet()
                .HasApiVersion(SharedKernelApiVersioningDefaults.DefaultApiVersion)
                .ReportApiVersions()
                .Build();

            app.MapGet("/echo-version", (HttpContext context) => context.RequestedApiVersion?.ToString())
                .WithApiVersionSet(apiVersionSet)
                .HasApiVersion(SharedKernelApiVersioningDefaults.DefaultApiVersion);

            app.MapGet("/v{version:apiVersion}/echo-version", (HttpContext context) => context.RequestedApiVersion?.ToString())
                .WithApiVersionSet(apiVersionSet)
                .HasApiVersion(SharedKernelApiVersioningDefaults.DefaultApiVersion);

            app.Start();
            return app;
        }
    }
}
