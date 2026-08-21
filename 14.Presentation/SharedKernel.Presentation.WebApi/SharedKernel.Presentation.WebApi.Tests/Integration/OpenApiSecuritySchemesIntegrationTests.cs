using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Presentation.WebApi.OpenApi;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="OpenApiExtensions.AddSharedKernelOpenApi"/>'s security-scheme
/// registration (WO-063, P-412) — asserted against the REAL generated OpenAPI JSON document, not a
/// mocked document model.
/// </summary>
public sealed class OpenApiSecuritySchemesIntegrationTests
{
    /// <summary>
    /// T-47: a document generated with only the default Bearer scheme active is byte-identical, in
    /// shape, to this method's original unconditional Bearer-only behavior — a single
    /// <c>securitySchemes</c> entry and a single-requirement <c>security</c> array.
    /// </summary>
    public sealed class DefaultBearerOnlyTests : IClassFixture<DefaultBearerOnlyTests.BearerOnlyFactory>
    {
        private readonly HttpClient _client;

        public DefaultBearerOnlyTests(BearerOnlyFactory factory) => _client = factory.CreateClient();

        [Fact]
        public async Task Document_DefaultConfiguration_RegistersOnlyBearerScheme()
        {
            using var document = await GetOpenApiDocumentAsync(_client);

            var securitySchemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");
            securitySchemes.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["Bearer"]);

            var scheme = securitySchemes.GetProperty("Bearer");
            scheme.GetProperty("type").GetString().Should().Be("http");
            scheme.GetProperty("scheme").GetString().Should().Be("bearer");
            scheme.GetProperty("bearerFormat").GetString().Should().Be("JWT");
        }

        [Fact]
        public async Task Document_DefaultConfiguration_SecurityArrayHasExactlyOneBearerRequirement()
        {
            using var document = await GetOpenApiDocumentAsync(_client);

            var security = document.RootElement.GetProperty("security");
            security.GetArrayLength().Should().Be(1);

            var requirement = security[0];
            requirement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["Bearer"]);
        }

        public sealed class BearerOnlyFactory : WebApplicationFactory<BearerOnlyFactory>
        {
            protected override IHost CreateHost(IHostBuilder builder)
            {
                var appBuilder = WebApplication.CreateBuilder();
                appBuilder.WebHost.UseTestServer();

                appBuilder.Services.AddRouting();
                // No configureSecuritySchemes callback — preserves the original unconditional
                // Bearer-only behavior.
                appBuilder.Services.AddSharedKernelOpenApi("Test API");

                var app = appBuilder.Build();
                app.MapGet("/ping", () => "pong");
                app.MapSharedKernelOpenApi();

                app.Start();
                return app;
            }
        }
    }

    /// <summary>
    /// T-48: enabling <c>ApiKey</c> and <c>MutualTls</c> alongside <c>Bearer</c> produces a correct
    /// combined <c>securitySchemes</c> dictionary and a <c>security</c> array with one requirement
    /// object per active scheme (OR semantics — never a single combined requirement object).
    /// </summary>
    public sealed class CombinedSchemesTests : IClassFixture<CombinedSchemesTests.CombinedSchemesFactory>
    {
        private readonly HttpClient _client;

        public CombinedSchemesTests(CombinedSchemesFactory factory) => _client = factory.CreateClient();

        [Fact]
        public async Task Document_AllThreeSchemesEnabled_SecuritySchemesDictionaryContainsAllThree()
        {
            using var document = await GetOpenApiDocumentAsync(_client);

            var securitySchemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");
            securitySchemes.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["Bearer", "ApiKey", "MutualTLS"]);

            var apiKeyScheme = securitySchemes.GetProperty("ApiKey");
            apiKeyScheme.GetProperty("type").GetString().Should().Be("apiKey");
            apiKeyScheme.GetProperty("name").GetString().Should().Be("X-Custom-Api-Key");
            apiKeyScheme.GetProperty("in").GetString().Should().Be("header");

            var mutualTlsScheme = securitySchemes.GetProperty("MutualTLS");
            mutualTlsScheme.GetProperty("type").GetString().Should().Be("mutualTLS");
        }

        [Fact]
        public async Task Document_AllThreeSchemesEnabled_SecurityArrayHasOneRequirementObjectPerScheme()
        {
            using var document = await GetOpenApiDocumentAsync(_client);

            var security = document.RootElement.GetProperty("security");

            // OR semantics: one requirement object per active scheme, never a single combined
            // requirement object (which would mean simultaneous/AND-required semantics).
            security.GetArrayLength().Should().Be(3);

            var schemeNamesPerRequirement = security
                .EnumerateArray()
                .Select(requirement => requirement.EnumerateObject().Single().Name)
                .ToArray();

            schemeNamesPerRequirement.Should().BeEquivalentTo(["Bearer", "ApiKey", "MutualTLS"]);
        }

        public sealed class CombinedSchemesFactory : WebApplicationFactory<CombinedSchemesFactory>
        {
            protected override IHost CreateHost(IHostBuilder builder)
            {
                var appBuilder = WebApplication.CreateBuilder();
                appBuilder.WebHost.UseTestServer();

                appBuilder.Services.AddRouting();
                appBuilder.Services.AddSharedKernelOpenApi(
                    "Test API",
                    configureSecuritySchemes: options =>
                    {
                        options.ApiKey = true;
                        options.ApiKeyHeaderName = "X-Custom-Api-Key";
                        options.MutualTls = true;
                    });

                var app = appBuilder.Build();
                app.MapGet("/ping", () => "pong");
                app.MapSharedKernelOpenApi();

                app.Start();
                return app;
            }
        }
    }

    private static async Task<JsonDocument> GetOpenApiDocumentAsync(HttpClient client)
    {
        var response = await client.GetAsync("/openapi/v1.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }
}
