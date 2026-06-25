using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Presentation.WebApi.OpenApi;
using SharedKernel.Presentation.WebApi.Versioning;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="OpenApiExtensions.AddSharedKernelOpenApi"/> /
/// <see cref="OpenApiExtensions.MapSharedKernelOpenApi"/> using a real in-process Minimal API host.
/// </summary>
/// <remarks>
/// Verifies a valid OpenAPI document is produced per registered version group and that the Scalar
/// interactive UI route responds successfully — both with and without API versioning enabled.
/// </remarks>
public sealed class OpenApiIntegrationTests
{
    public sealed class WithVersioningTests : IClassFixture<WithVersioningTests.OpenApiWithVersioningFactory>
    {
        private readonly HttpClient _client;

        public WithVersioningTests(OpenApiWithVersioningFactory factory) => _client = factory.CreateClient();

        [Fact]
        public async Task OpenApiDocument_ForRegisteredVersionGroup_ReturnsValidJsonDocument()
        {
            var response = await _client.GetAsync("/openapi/v1.json");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("\"openapi\"");
        }

        [Fact]
        public async Task ScalarRoute_RespondsSuccessfully()
        {
            var response = await _client.GetAsync("/scalar/v1");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        public sealed class OpenApiWithVersioningFactory : WebApplicationFactory<OpenApiWithVersioningFactory>
        {
            protected override IHost CreateHost(IHostBuilder builder)
            {
                var appBuilder = WebApplication.CreateBuilder();
                appBuilder.WebHost.UseTestServer();

                appBuilder.Services.AddRouting();
                appBuilder.Services.AddSharedKernelApiVersioning();
                appBuilder.Services.AddSharedKernelOpenApi("Test API");

                var app = appBuilder.Build();

                var versionSet = app.NewApiVersionSet()
                    .HasApiVersion(SharedKernelApiVersioningDefaults.DefaultApiVersion)
                    .ReportApiVersions()
                    .Build();

                app.MapGet("/ping", () => "pong")
                    .WithApiVersionSet(versionSet)
                    .HasApiVersion(SharedKernelApiVersioningDefaults.DefaultApiVersion);

                app.MapSharedKernelOpenApi();

                app.Start();
                return app;
            }
        }
    }

    public sealed class WithoutVersioningTests : IClassFixture<WithoutVersioningTests.OpenApiWithoutVersioningFactory>
    {
        private readonly HttpClient _client;

        public WithoutVersioningTests(OpenApiWithoutVersioningFactory factory) => _client = factory.CreateClient();

        [Fact]
        public async Task OpenApiDocument_FallbackSingleDocument_ReturnsValidJsonDocument()
        {
            var response = await _client.GetAsync("/openapi/v1.json");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("\"openapi\"");
        }

        [Fact]
        public async Task ScalarRoute_RespondsSuccessfully()
        {
            var response = await _client.GetAsync("/scalar/v1");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        public sealed class OpenApiWithoutVersioningFactory : WebApplicationFactory<OpenApiWithoutVersioningFactory>
        {
            protected override IHost CreateHost(IHostBuilder builder)
            {
                var appBuilder = WebApplication.CreateBuilder();
                appBuilder.WebHost.UseTestServer();

                appBuilder.Services.AddRouting();
                appBuilder.Services.AddSharedKernelOpenApi("Test API");

                var app = appBuilder.Build();

                app.MapGet("/ping", () => "pong");
                app.MapSharedKernelOpenApi();

                app.Start();
                return app;
            }
        }
    }
}
